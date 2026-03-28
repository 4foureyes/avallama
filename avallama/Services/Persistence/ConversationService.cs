// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using avallama.Models;
using avallama.Utilities;
using Microsoft.Data.Sqlite;

namespace avallama.Services.Persistence;

internal static class Roles
{
    public const string User = "user";
    public const string Assistant = "assistant";
}

public interface IConversationService
{
    Task<Guid> CreateConversation(Conversation conversation);
    Task<long> InsertMessage(Guid conversationId, Message message, string? modelName, double? tokenPerSec);
    Task DeleteMessage(long messageId);
    Task<IList<Conversation>> GetConversations();
    Task<bool> UpdateConversationTitle(Conversation conversation);
    Task<IList<Message>> GetMessagesForConversation(Conversation conversation);
    Task DeleteConversation(Guid conversationId);
}

public class ConversationService : IConversationService, IDisposable
{
    private readonly SqliteConnection _connection;

    public ConversationService(SqliteConnection? testConnection = null)
    {
        if (testConnection is not null)
        {
            _connection = testConnection;
            _connection.Open();
            return;
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var appDir = Path.Combine(appData, "avallama");
        if (!Directory.Exists(appDir))
            Directory.CreateDirectory(appDir);

        var dbPath = Path.Combine(appDir, "avallama.db");
        var connectionString = $"Data Source={dbPath}";
        _connection = new SqliteConnection(connectionString);
        _connection.Open();
    }

    public async Task<Guid> CreateConversation(Conversation conversation)
    {
        var conversationId = Guid.NewGuid();
        using (DatabaseLock.Instance.AcquireWriteLock())
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText =
                "INSERT INTO conversations (id, title, created_at, last_message_sent_at) VALUES (@Id, @Title, @CreatedAt, @LastMessageSentAt)";
            cmd.Parameters.AddWithValue("@Id", conversationId);
            cmd.Parameters.AddWithValue("@Title", conversation.Title);
            cmd.Parameters.AddWithValue("@CreatedAt", DateTime.Now);
            cmd.Parameters.AddWithValue("@LastMessageSentAt", DateTime.Now);

            try
            {
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception)
            {
                // TODO: InterruptService
                throw;
            }
        }

        return conversationId;
    }

    public async Task<long> InsertMessage(Guid conversationId, Message message, string? modelName, double? tokenPerSec)
    {
        if (message is FailedMessage) return -1;
        long newId = 0;

        using (DatabaseLock.Instance.AcquireWriteLock())
        {
            await using var transaction = await _connection.BeginTransactionAsync();
            var now = DateTime.Now;

            try
            {
                await using var insertCmd = _connection.CreateCommand();
                // Create transaction to ensure both inserts succeed or fail together
                insertCmd.Transaction = (SqliteTransaction)transaction;

                // Insert message
                insertCmd.CommandText =
                    """
                    INSERT INTO messages (conversation_id, role, message, timestamp, model_name, tokens_per_sec)
                    VALUES (@ConversationId, @Role, @Message, @Timestamp, @ModelName, @TokenPerSec);
                    SELECT last_insert_rowid()
                    """;
                insertCmd.Parameters.AddWithValue("@ConversationId", conversationId);
                insertCmd.Parameters.AddWithValue("@Role", message is GeneratedMessage ? "assistant" : "user");
                insertCmd.Parameters.AddWithValue("@Message", message.Content);
                insertCmd.Parameters.AddWithValue("@Timestamp", now);
                insertCmd.Parameters.AddWithValue("@ModelName", modelName ?? (object)DBNull.Value);
                insertCmd.Parameters.AddWithValue("@TokenPerSec", tokenPerSec ?? (object)DBNull.Value);
                var result = await insertCmd.ExecuteScalarAsync();
                newId = Convert.ToInt64(result);

                // Update conversation's last_message_sent_at
                await using var updateCmd = _connection.CreateCommand();
                updateCmd.Transaction = (SqliteTransaction)transaction;
                updateCmd.CommandText =
                    "UPDATE conversations SET last_message_sent_at = @LastMessageSentAt WHERE id = @ConversationId";
                updateCmd.Parameters.AddWithValue("@LastMessageSentAt", now);
                updateCmd.Parameters.AddWithValue("@ConversationId", conversationId);
                await updateCmd.ExecuteNonQueryAsync();

                // Commit transaction
                await transaction.CommitAsync();
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                // TODO: InterruptService
                throw;
            }
        }
        // Update model's token/sec (this cannot be in the transaction, as it requires a query to calculate)
        if (message is not GeneratedMessage || modelName is null)
        {
            return newId;
        }

        var avgTokenPerSec = await CalculateModelAvgTokenPerSecAsync(modelName);
        await UpdateModelAvgTokenPerSecAsync(avgTokenPerSec, modelName);
        return newId;
    }

    private async Task<double> CalculateModelAvgTokenPerSecAsync(string modelName)
    {
        List<double> tokenPerSecValues = [];
        using (DatabaseLock.Instance.AcquireReadLock())
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText =
                "SELECT tokens_per_sec FROM messages WHERE model_name IS NOT NULL AND model_name = @ModelName";
            cmd.Parameters.AddWithValue("@ModelName", modelName);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (!reader.HasRows) return 0;

            try
            {
                while (await reader.ReadAsync())
                {
                    tokenPerSecValues.Add((double)reader["tokens_per_sec"]);
                }
            }
            catch (Exception)
            {
                // TODO: InterruptService
                throw;
            }
        }
        return tokenPerSecValues.Average();
    }

    private async Task UpdateModelAvgTokenPerSecAsync(double avgTokenPerSec, string modelName)
    {
        using (DatabaseLock.Instance.AcquireWriteLock())
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = "UPDATE ollama_models SET avg_token_per_sec = @AverageTokenPerSec WHERE name = @ModelName";
            cmd.Parameters.AddWithValue("@AverageTokenPerSec", avgTokenPerSec);
            cmd.Parameters.AddWithValue("@ModelName", modelName);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    public async Task<IList<Conversation>> GetConversations()
    {
        ObservableStack<Conversation> conversations;
        using (DatabaseLock.Instance.AcquireReadLock())
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT * FROM conversations ORDER BY last_message_sent_at DESC";
            await using var reader = await cmd.ExecuteReaderAsync();
            conversations = [];
            var conversationIds = new List<Guid>();
            if (!reader.HasRows) return conversations;

            try
            {
                while (await reader.ReadAsync())
                {
                    var id = Guid.Parse((string)reader["id"]);
                    var conversation = new Conversation(
                        Guid.Parse((string)reader["id"]),
                        (string)reader["title"],
                        new List<Message>()
                    );
                    conversations.Add(conversation);
                    conversationIds.Add(id);
                }
            }
            catch (Exception)
            {
                // TODO: InterruptService
                throw;
            }

            var lastModels = await GetLastModelsForConversationsAsync(conversationIds);

            foreach (var conv in conversations)
            {
                conv.Model =
                    lastModels.GetValueOrDefault(conv.ConversationId,
                        "llama3.2:2b"); // this default could be set in settings?
            }
        }

        return conversations;
    }

    private async Task<Dictionary<Guid, string>> GetLastModelsForConversationsAsync(IEnumerable<Guid> conversationIds)
    {
        // Semaphore is already held by caller

        var lastModels = new Dictionary<Guid, string>();

        var enumerable = conversationIds as Guid[] ?? conversationIds.ToArray();
        if (enumerable.Length == 0)
            return lastModels;

        var idsParam = string.Join(",", enumerable.Select((_, i) => $"@id{i}"));
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"""
                               SELECT conversation_id, model_name
                               FROM (
                                   SELECT conversation_id, model_name,
                                          ROW_NUMBER() OVER (PARTITION BY conversation_id ORDER BY timestamp DESC) AS rn
                                   FROM messages
                                   WHERE conversation_id IN ({idsParam})
                               )
                               WHERE rn = 1
                           """;


        var index = 0;
        foreach (var id in enumerable)
            cmd.Parameters.AddWithValue($"@id{index++}", id.ToString());

        try
        {
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var convId = Guid.Parse((string)reader["conversation_id"]);
                var model = reader.IsDBNull(1) ? "llama3.2:2b" : (string)reader["model_name"];
                lastModels[convId] = model;
            }
        }
        catch (Exception)
        {
            // TODO: InterruptService
            throw;
        }

        return lastModels;
    }


    public async Task<bool> UpdateConversationTitle(Conversation conversation)
    {
        using (DatabaseLock.Instance.AcquireWriteLock())
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = "UPDATE conversations SET title = @title WHERE id = @ConversationId";
            cmd.Parameters.AddWithValue("@title", conversation.Title);
            cmd.Parameters.AddWithValue("@ConversationId", conversation.ConversationId);
            try
            {
                var res = await cmd.ExecuteNonQueryAsync();
                return res > 0;
            }
            catch (Exception)
            {
                // TODO: InterruptService
                throw;
            }
        }
    }

    public async Task<IList<Message>> GetMessagesForConversation(Conversation conversation)
    {
        ObservableCollection<Message> messages;
        using (DatabaseLock.Instance.AcquireReadLock())
        {
            messages = [];
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT * FROM messages WHERE conversation_id = @ConversationId ORDER BY timestamp";
            cmd.Parameters.AddWithValue("@ConversationId", conversation.ConversationId);
            await using var reader = await cmd.ExecuteReaderAsync();
            try
            {
                while (await reader.ReadAsync())
                {
                    var id = reader.GetInt64(0);
                    var role = reader["role"].ToString();
                    var content = reader["message"].ToString()!;

                    var message = role switch
                    {
                        Roles.User => new Message(content),
                        Roles.Assistant => new GeneratedMessage(content, (double)reader["tokens_per_sec"]),
                        _ => throw new InvalidOperationException($"Unknown role: {role}")
                    };

                    message.Id = id;
                    messages.Add(message);
                }
            }
            catch (Exception)
            {
                // TODO: InterruptService
                throw;
            }
        }

        return messages;
    }

    public async Task DeleteConversation(Guid conversationId)
    {
        using (DatabaseLock.Instance.AcquireWriteLock())
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = "DELETE FROM conversations WHERE id = @ConversationId";
            cmd.Parameters.AddWithValue("@ConversationId", conversationId);
            try
            {
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception)
            {
                // TODO: InterruptService
                throw;
            }
        }
    }

    public async Task DeleteMessage(long messageId)
    {
        using (DatabaseLock.Instance.AcquireWriteLock())
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = "DELETE FROM messages WHERE id = @Id";
            cmd.Parameters.AddWithValue("@Id", messageId);
            try
            {
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception)
            {
                // TODO: InterruptService
                throw;
            }
        }
    }

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
