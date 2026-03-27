// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using avallama.Models;
using avallama.Services;
using avallama.Services.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace avallama.Tests.Services.Persistence;

[Collection("Database Tests")]
public class ConversationServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ConversationService _conversationService;

    public ConversationServiceTests()
    {
        var tempDbPath = Path.Combine(Path.GetTempPath(), $"avallama_test_{Guid.NewGuid()}.db");
        _connection = new SqliteConnection($"Data Source={tempDbPath}");
        _connection.Open();

        InitializeTestSchema(_connection);

        _conversationService = new ConversationService(_connection);
    }

    private static void InitializeTestSchema(SqliteConnection connection)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = """
                          PRAGMA foreign_keys = ON;
                          PRAGMA journal_mode = WAL;
                          PRAGMA synchronous = NORMAL;
                          PRAGMA temp_store = MEMORY;
                          PRAGMA cache_size = 32000;

                          CREATE TABLE IF NOT EXISTS conversations (
                          id TEXT PRIMARY KEY,
                          title TEXT NOT NULL,
                          created_at TEXT NOT NULL,
                          last_message_sent_at TEXT
                          );

                          CREATE TABLE IF NOT EXISTS messages (
                          id INTEGER PRIMARY KEY AUTOINCREMENT,
                          conversation_id TEXT NOT NULL,
                          role TEXT NOT NULL,
                          message TEXT NOT NULL,
                          timestamp TEXT NOT NULL,
                          model_name TEXT,
                          tokens_per_sec REAL,
                          FOREIGN KEY (conversation_id) REFERENCES conversations(id) ON DELETE CASCADE
                          );

                          CREATE INDEX IF NOT EXISTS idx_messages_convo_timestamp
                          ON messages (conversation_id, timestamp);

                          CREATE INDEX IF NOT EXISTS idx_conversations_last_msg
                          ON conversations (last_message_sent_at);

                          CREATE TABLE IF NOT EXISTS model_families (
                          name TEXT PRIMARY KEY,
                          description TEXT NOT NULL,
                          pull_count INTEGER NOT NULL DEFAULT 0,
                          labels TEXT,
                          tag_count INTEGER NOT NULL DEFAULT 0,
                          last_updated TEXT,
                          cached_at TEXT NOT NULL,
                          UNIQUE(name)
                          );

                          CREATE TABLE IF NOT EXISTS ollama_models (
                          name TEXT PRIMARY KEY,
                          family_name TEXT NOT NULL,
                          parameters INTEGER,
                          size INTEGER NOT NULL,
                          format TEXT,
                          quantization TEXT,
                          architecture TEXT,
                          block_count INTEGER,
                          context_length INTEGER,
                          embedding_length INTEGER,
                          additional_info TEXT,
                          is_downloaded INTEGER NOT NULL DEFAULT 0,
                          avg_token_per_sec REAL,
                          cached_at TEXT NOT NULL,
                          FOREIGN KEY (family_name) REFERENCES model_families(name) ON DELETE CASCADE
                          );
                          """;
        cmd.ExecuteNonQuery();
    }

    // Conversation tests

    [Fact]
    public async Task CreateConversation_ShouldComplete_Within100ms()
    {
        var conversation = new Conversation(Guid.AllBitsSet, "Test Conversation", new List<Message>());
        var stopwatch = Stopwatch.StartNew();
        await _conversationService.CreateConversation(conversation);
        stopwatch.Stop();

        Assert.True(stopwatch.ElapsedMilliseconds < 100, $"Operation took too long: {stopwatch.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task GetMessagesForConversation_ShouldReturnMessagesInOrder()
    {
        var id = await _conversationService.CreateConversation(
            new Conversation(Guid.Empty, "Test", new List<Message>()));

        await _conversationService.InsertMessage(id, new Message("First"), null, null);
        await Task.Delay(10);
        await _conversationService.InsertMessage(id, new GeneratedMessage("Second", 50.0), "model", 50.0);
        await Task.Delay(10);
        await _conversationService.InsertMessage(id, new Message("Third"), null, null);

        var messages = await _conversationService.GetMessagesForConversation(
            new Conversation(id, "Test", new List<Message>()));

        Assert.Equal(3, messages.Count);
        Assert.Equal("First", messages[0].Content);
        Assert.Equal("Second", messages[1].Content);
        Assert.Equal("Third", messages[2].Content);
        Assert.IsType<GeneratedMessage>(messages[1]);
    }

    [Fact]
    public async Task GetMessagesForConversation_WithNoMessages_ShouldReturnEmpty()
    {
        var id = await _conversationService.CreateConversation(
            new Conversation(Guid.Empty, "Empty", new List<Message>()));

        var messages = await _conversationService.GetMessagesForConversation(
            new Conversation(id, "Empty", new List<Message>()));

        Assert.Empty(messages);
    }

    [Fact]
    public async Task GetConversations_ShouldOrderByLastMessage()
    {
        var id1 = await _conversationService.CreateConversation(
            new Conversation(Guid.Empty, "First", new List<Message>()));
        await Task.Delay(10);

        var id2 = await _conversationService.CreateConversation(
            new Conversation(Guid.Empty, "Second", new List<Message>()));
        await Task.Delay(10);

        // Add message to first conversation to make it most recent
        await _conversationService.InsertMessage(id1, new Message("Update"), null, null);

        var conversations = await _conversationService.GetConversations();

        Assert.Equal(id1, conversations.First().ConversationId);
        Assert.Equal(id2, conversations.ElementAt(1).ConversationId);
    }

    [Fact]
    public async Task CreateConversation_WithSpecialCharactersInTitle_ShouldSucceed()
    {
        const string title = "Test's \"Conversation\" with <special> & chars!";
        var id = await _conversationService.CreateConversation(
            new Conversation(Guid.Empty, title, new List<Message>()));

        var conversations = await _conversationService.GetConversations();
        var found = conversations.FirstOrDefault(c => c.ConversationId == id);

        Assert.NotNull(found);
        Assert.Equal(title, found.Title);
    }

    [Fact]
    public async Task GetConversations_WithLargeDataset_ShouldComplete_Within1000ms()
    {
        for (var i = 0; i < 10_000; i++)
        {
            await _conversationService.CreateConversation(
                new Conversation(Guid.Empty, $"Conv {i}", new List<Message>()));
        }

        var stopwatch = Stopwatch.StartNew();
        var conversations = await _conversationService.GetConversations();
        stopwatch.Stop();

        Assert.NotEmpty(conversations);
        Assert.True(stopwatch.ElapsedMilliseconds < 1000,
            $"Operation took too long: {stopwatch.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task DeleteConversation_ShouldCascadeDeleteMessages()
    {
        var id = await _conversationService.CreateConversation(
            new Conversation(Guid.Empty, "Test", new List<Message>()));

        await _conversationService.InsertMessage(id, new Message("Message 1"), null, null);
        await _conversationService.InsertMessage(id, new Message("Message 2"), null, null);

        // Delete conversation
        var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM conversations WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id.ToString());
        await cmd.ExecuteNonQueryAsync();

        // Verify messages were cascade deleted
        var msgCmd = _connection.CreateCommand();
        msgCmd.CommandText = "SELECT COUNT(*) FROM messages WHERE conversation_id = @id";
        msgCmd.Parameters.AddWithValue("@id", id.ToString());
        var count = (long)(await msgCmd.ExecuteScalarAsync() ?? 0L);

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task UpdateConversationTitle_WithValidId_ShouldReturnTrue()
    {
        var id = await _conversationService.CreateConversation(
            new Conversation(Guid.Empty, "Original Title", new List<Message>()));

        var updatedConversation = new Conversation(id, "Updated Title", new List<Message>());
        var result = await _conversationService.UpdateConversationTitle(updatedConversation);

        Assert.True(result);

        var conversations = await _conversationService.GetConversations();
        var found = conversations.FirstOrDefault(c => c.ConversationId == id);
        Assert.NotNull(found);
        Assert.Equal("Updated Title", found.Title);
    }

    [Fact]
    public async Task UpdateConversationTitle_WithInvalidId_ShouldReturnFalse()
    {
        var nonExistentConversation = new Conversation(Guid.NewGuid(), "Title", new List<Message>());
        var result = await _conversationService.UpdateConversationTitle(nonExistentConversation);

        Assert.False(result);
    }

    // Message tests

    [Fact]
    public async Task InsertMessage_ShouldComplete_Within50ms()
    {
        // Ensure we have a conversation to add message to so foreign key constraint doesn't fail
        var conversation = new Conversation(Guid.AllBitsSet, "Test Conversation", new List<Message>());
        var conversationId = await _conversationService.CreateConversation(conversation);

        var message = new Message("Test Message");
        var stopwatch = Stopwatch.StartNew();
        await _conversationService.InsertMessage(conversationId, message, null, null);
        stopwatch.Stop();

        Assert.True(stopwatch.ElapsedMilliseconds < 50, $"Operation took too long: {stopwatch.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task InsertMessage_WithFailedMessage_ShouldNotInsert()
    {
        var id = await _conversationService.CreateConversation(
            new Conversation(Guid.Empty, "Test", new List<Message>()));

        await _conversationService.InsertMessage(id, new FailedMessage(), null, null);

        var messages = await _conversationService.GetMessagesForConversation(
            new Conversation(id, "Test", new List<Message>()));

        Assert.Empty(messages);
    }

    [Fact]
    public async Task InsertMessage_ShouldHandle_ConcurrentWrites()
    {
        var conversationId = await _conversationService.CreateConversation(
            new Conversation(Guid.Empty, "Concurrent Test", new List<Message>()));

        var tasks = Enumerable.Range(0, 100)
            .Select(i => _conversationService.InsertMessage(
                conversationId,
                new Message($"Message {i}"),
                "llama3.2",
                50.0));

        // Should not throw exceptions
        await Task.WhenAll(tasks);

        var messages = await _conversationService.GetMessagesForConversation(
            new Conversation(conversationId, "Test", new List<Message>()));
        Assert.Equal(100, messages.Count);
    }

    [Fact]
    public async Task InsertMessage_WithVeryLongContent_ShouldSucceed()
    {
        var id = await _conversationService.CreateConversation(
            new Conversation(Guid.Empty, "Test", new List<Message>()));

        var longContent = new string('a', 100_000);
        await _conversationService.InsertMessage(id, new Message(longContent), null, null);

        var messages = await _conversationService.GetMessagesForConversation(
            new Conversation(id, "Test", new List<Message>()));

        Assert.Single(messages);
        Assert.Equal(longContent, messages[0].Content);
    }

    [Fact]
    public async Task InsertMessage_WithGeneratedMessages_ShouldUpdateModelAverageTokenPerSec()
    {
        const string familyName = "llama-family";
        const string modelName = "llama3.2:3b";
        await SetupModelAsync(familyName, modelName);

        var conversationId = await _conversationService.CreateConversation(
            new Conversation(Guid.Empty, "Average Test", new List<Message>()));

        await _conversationService.InsertMessage(
            conversationId,
            new GeneratedMessage("First", 10.0),
            modelName,
            10.0);

        Assert.Equal(10.0, await GetModelAvgTokenPerSecAsync(modelName));

        await _conversationService.InsertMessage(
            conversationId,
            new GeneratedMessage("Second", 20.0),
            modelName,
            20.0);

        Assert.Equal(15.0, await GetModelAvgTokenPerSecAsync(modelName));
    }

    [Fact]
    public async Task InsertMessage_WithDifferentModels_ShouldCalculateAveragesIndependently()
    {
        const string familyName = "llama-family";
        const string firstModel = "llama3.2:3b";
        const string secondModel = "mistral:7b";
        await SetupModelAsync(familyName, firstModel);
        await SetupModelAsync(familyName, secondModel);

        var conversationId = await _conversationService.CreateConversation(
            new Conversation(Guid.Empty, "Per Model Average", new List<Message>()));

        await _conversationService.InsertMessage(
            conversationId,
            new GeneratedMessage("A1", 10.0),
            firstModel,
            10.0);
        await _conversationService.InsertMessage(
            conversationId,
            new GeneratedMessage("B1", 40.0),
            secondModel,
            40.0);
        await _conversationService.InsertMessage(
            conversationId,
            new GeneratedMessage("A2", 20.0),
            firstModel,
            20.0);

        Assert.Equal(15.0, await GetModelAvgTokenPerSecAsync(firstModel));
        Assert.Equal(40.0, await GetModelAvgTokenPerSecAsync(secondModel));
    }

    [Fact]
    public async Task InsertMessage_WithUserMessage_ShouldNotUpdateModelAverageTokenPerSec()
    {
        const string familyName = "llama-family";
        const string modelName = "llama3.2:3b";
        await SetupModelAsync(familyName, modelName, avgTokenPerSec: 12.0);

        var conversationId = await _conversationService.CreateConversation(
            new Conversation(Guid.Empty, "User Message Average", new List<Message>()));

        await _conversationService.InsertMessage(
            conversationId,
            new Message("User prompt"),
            null,
            null);

        Assert.Equal(12.0, await GetModelAvgTokenPerSecAsync(modelName));
    }

    [Fact]
    public async Task InsertMessage_WithFailedMessage_ShouldNotUpdateModelAverageTokenPerSec()
    {
        const string familyName = "llama-family";
        const string modelName = "llama3.2:3b";
        await SetupModelAsync(familyName, modelName, avgTokenPerSec: 25.0);

        var conversationId = await _conversationService.CreateConversation(
            new Conversation(Guid.Empty, "Failed Message Average", new List<Message>()));

        var insertedId = await _conversationService.InsertMessage(
            conversationId,
            new FailedMessage(),
            modelName,
            42.0);

        Assert.Equal(-1, insertedId);
        Assert.Equal(25.0, await GetModelAvgTokenPerSecAsync(modelName));
    }

    [Fact]
    public async Task DeleteConversation_DeletesConversation()
    {
        var id = await _conversationService.CreateConversation(
            new Conversation(Guid.Empty, "Test", new List<Message>()));
        await _conversationService.DeleteConversation(id);
        var conversations = await _conversationService.GetConversations();
        Assert.DoesNotContain(conversations, c => c.ConversationId == id);
    }

    [Fact]
    public async Task DeleteConversation_DeletesConversationMessages()
    {
        var id = await _conversationService.CreateConversation(
            new Conversation(Guid.Empty, "Test", new List<Message>()));
        await _conversationService.InsertMessage(id, new Message("Message 1"), null, null);
        await _conversationService.DeleteConversation(id);
        var messages = await _conversationService.GetMessagesForConversation(
            new Conversation(id, "Test", new List<Message>()));
        Assert.Empty(messages);
    }

    private async Task SetupModelAsync(string familyName, string modelName, double? avgTokenPerSec = null)
    {
        var now = DateTime.Now;

        await using var familyCmd = _connection.CreateCommand();
        familyCmd.CommandText =
            "INSERT OR IGNORE INTO model_families (name, description, pull_count, tag_count, cached_at) VALUES (@Name, @Description, 0, 0, @CachedAt)";
        familyCmd.Parameters.AddWithValue("@Name", familyName);
        familyCmd.Parameters.AddWithValue("@Description", "Test model family");
        familyCmd.Parameters.AddWithValue("@CachedAt", now);
        await familyCmd.ExecuteNonQueryAsync();

        await using var modelCmd = _connection.CreateCommand();
        modelCmd.CommandText =
            "INSERT INTO ollama_models (name, family_name, size, avg_token_per_sec, cached_at) VALUES (@Name, @FamilyName, 1, @AvgTokenPerSec, @CachedAt)";
        modelCmd.Parameters.AddWithValue("@Name", modelName);
        modelCmd.Parameters.AddWithValue("@FamilyName", familyName);
        modelCmd.Parameters.AddWithValue("@AvgTokenPerSec", avgTokenPerSec ?? (object)DBNull.Value);
        modelCmd.Parameters.AddWithValue("@CachedAt", now);
        await modelCmd.ExecuteNonQueryAsync();
    }

    private async Task<double?> GetModelAvgTokenPerSecAsync(string modelName)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT avg_token_per_sec FROM ollama_models WHERE name = @ModelName";
        cmd.Parameters.AddWithValue("@ModelName", modelName);
        var result = await cmd.ExecuteScalarAsync();

        if (result is null || result == DBNull.Value)
        {
            return null;
        }

        return Convert.ToDouble(result);
    }

    public void Dispose()
    {
        var dbPath = _connection.DataSource;

        _connection.Close();
        _connection.Dispose();

        try
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
            if (File.Exists(dbPath + "-wal")) File.Delete(dbPath + "-wal");
            if (File.Exists(dbPath + "-shm")) File.Delete(dbPath + "-shm");
        }
        catch
        {
            // ignore
        }

        GC.SuppressFinalize(this);
    }
}
