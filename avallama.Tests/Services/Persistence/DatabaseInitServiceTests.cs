// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using avallama.Services.Persistence;
using avallama.Tests.Extensions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace avallama.Tests.Services.Persistence;

[Collection("Database Tests")]
public class DatabaseInitServiceTests
{
    [Fact]
    public async Task InitializeSchemaAsync_DoesNotThrow()
    {
        var service = new DatabaseInitService(isTest: true);
        await AsyncAssertExtensions.DoesNotThrowAsync(async () =>
        {
            await using var conn = await service.InitializeDatabaseAsync();
            Assert.NotNull(conn);
            Assert.Equal(System.Data.ConnectionState.Open, conn.State);
        });
    }

    [Fact]
    public async Task InitializeSchemaAsync_CreatesShemaMetadata_WithCorrectHash()
    {
        var service = new DatabaseInitService();

        await using var conn = await service.InitializeDatabaseAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM schema_metadata WHERE key = 'schema_hash'";
        var hash = await cmd.ExecuteScalarAsync() as string;

        Assert.NotNull(hash);
        Assert.NotEmpty(hash);
        Assert.Equal(128, hash.Length);
    }

    [Fact]
    public async Task InitializeSchemaAsync_MessagesIndex_IsUsed_WhenQueryingMessages_ForConversation()
    {
        var service = new DatabaseInitService(isTest: true);
        await using var conn = await service.InitializeDatabaseAsync();

        await using (var insertCmd = conn.CreateCommand())
        {
            insertCmd.CommandText = """
                                    INSERT INTO conversations (id, title, created_at, last_message_sent_at)
                                    VALUES ('conv-1', 'Test', '2024-01-01T00:00:00Z', '2024-01-01T01:00:00Z');

                                    INSERT INTO messages (conversation_id, role, message, timestamp)
                                    VALUES ('conv-1', 'user', 'Test', '2024-01-01T00:00:00Z');
                                    """;
            await insertCmd.ExecuteNonQueryAsync();
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "EXPLAIN QUERY PLAN SELECT * FROM messages WHERE conversation_id = 'conv-1' ORDER BY timestamp";
        await using var reader = await cmd.ExecuteReaderAsync();

        var queryPlan = new List<string>();
        while (await reader.ReadAsync())
        {
            queryPlan.Add(reader.GetString(3));
        }

        var planText = string.Join(" ", queryPlan);
        Assert.Contains("idx_messages_convo_timestamp", planText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InitializeSchemaAsync_ConversationsIndex_IsUsed_WhenQueryingByLastMessage()
    {
        var service = new DatabaseInitService(isTest: true);
        await using var conn = await service.InitializeDatabaseAsync();

        await using (var insertCmd = conn.CreateCommand())
        {
            insertCmd.CommandText = """
                                    INSERT INTO conversations (id, title, created_at, last_message_sent_at)
                                    VALUES ('conv-1', 'Test', '2024-01-01T00:00:00Z', '2024-01-01T01:00:00Z');
                                    """;
            await insertCmd.ExecuteNonQueryAsync();
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "EXPLAIN QUERY PLAN SELECT * FROM conversations WHERE last_message_sent_at IS NOT NULL ORDER BY last_message_sent_at DESC";
        await using var reader = await cmd.ExecuteReaderAsync();

        var queryPlan = new List<string>();
        while (await reader.ReadAsync())
        {
            queryPlan.Add(reader.GetString(3));
        }

        var planText = string.Join(" ", queryPlan);
        Assert.Contains("idx_conversations_last_msg", planText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InitializeSchemaAsync_ModelFamiliesIndex_IsUsed_WhenQueryingByPullCount()
    {
        var service = new DatabaseInitService(isTest: true);
        await using var conn = await service.InitializeDatabaseAsync();

        await using (var insertCmd = conn.CreateCommand())
        {
            insertCmd.CommandText = """
                                    INSERT INTO model_families (name, description, pull_count, tag_count, cached_at)
                                    VALUES ('family-1', 'Test Family', 1024, 0, '2024-01-01T00:00:00Z');
                                    """;
            await insertCmd.ExecuteNonQueryAsync();
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXPLAIN QUERY PLAN SELECT * FROM model_families ORDER BY pull_count DESC";
        await using var reader = await cmd.ExecuteReaderAsync();
        var queryPlan = new List<string>();
        while (await reader.ReadAsync())
        {
            queryPlan.Add(reader.GetString(3));
        }

        var planText = string.Join(" ", queryPlan);
        Assert.Contains("idx_models_pull_count_desc", planText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InitializeSchemaAsync_OllamaModelsIndex_IsUsed_WhenQueryingByName()
    {
        var service = new DatabaseInitService(isTest: true);
        await using var conn = await service.InitializeDatabaseAsync();

        await using (var insertCmd = conn.CreateCommand())
        {
            insertCmd.CommandText = """
                                    INSERT INTO model_families (name, description, pull_count, tag_count, cached_at)
                                    VALUES ('family-1', 'Test Family', 1024, 0, '2024-01-01T00:00:00Z');
                                    """;
            await insertCmd.ExecuteNonQueryAsync();
        }

        await using (var insertCmd = conn.CreateCommand())
        {
            insertCmd.CommandText = """
                                    INSERT INTO ollama_models (name, family_name, size, cached_at)
                                    VALUES ('model-1', 'family-1', 1024, '2024-01-01T00:00:00Z');
                                    """;
            await insertCmd.ExecuteNonQueryAsync();
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXPLAIN QUERY PLAN SELECT * FROM ollama_models ORDER BY name ASC";
        await using var reader = await cmd.ExecuteReaderAsync();
        var queryPlan = new List<string>();
        while (await reader.ReadAsync())
        {
            queryPlan.Add(reader.GetString(3));
        }

        var planText = string.Join(" ", queryPlan);
        Assert.Contains("idx_models_name_asc", planText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MigrateSchemaAsync_LegacyDownloadStatus_MigratesToIsDownloaded_AndAddsAvgTokenPerSec()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();

        await using (var setupCmd = conn.CreateCommand())
        {
            setupCmd.CommandText = """
                                   CREATE TABLE model_families (
                                       name TEXT PRIMARY KEY,
                                       description TEXT NOT NULL,
                                       pull_count INTEGER NOT NULL DEFAULT 0,
                                       labels TEXT,
                                       tag_count INTEGER NOT NULL DEFAULT 0,
                                       last_updated TEXT,
                                       cached_at TEXT NOT NULL
                                   );

                                   CREATE TABLE ollama_models (
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
                                       download_status INTEGER NOT NULL,
                                       cached_at TEXT NOT NULL,
                                       FOREIGN KEY (family_name) REFERENCES model_families(name) ON DELETE CASCADE
                                   );

                                   INSERT INTO model_families (name, description, pull_count, tag_count, cached_at)
                                   VALUES ('family-1', 'Test Family', 1, 1, '2024-01-01T00:00:00Z');

                                   INSERT INTO ollama_models (name, family_name, size, download_status, cached_at)
                                   VALUES ('model-1', 'family-1', 100, 5, '2024-01-01T00:00:00Z');
                                   """;
            await setupCmd.ExecuteNonQueryAsync();
        }

        await InvokeMigrateSchemaAsync(conn);

        Assert.False(await HasColumnAsync(conn, "ollama_models", "download_status"));
        Assert.True(await HasColumnAsync(conn, "ollama_models", "is_downloaded"));
        Assert.True(await HasColumnAsync(conn, "ollama_models", "avg_token_per_sec"));

        await using var verifyCmd = conn.CreateCommand();
        verifyCmd.CommandText = "SELECT is_downloaded FROM ollama_models WHERE name = 'model-1'";
        var migratedFlag = (long)(await verifyCmd.ExecuteScalarAsync() ?? 0L);
        Assert.Equal(1L, migratedFlag);
    }

    [Fact]
    public async Task MigrateSchemaAsync_MissingAvgTokenPerSec_AddsColumnWithoutLegacyMigration()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();

        await using (var setupCmd = conn.CreateCommand())
        {
            setupCmd.CommandText = """
                                   CREATE TABLE model_families (
                                       name TEXT PRIMARY KEY,
                                       description TEXT NOT NULL,
                                       pull_count INTEGER NOT NULL DEFAULT 0,
                                       labels TEXT,
                                       tag_count INTEGER NOT NULL DEFAULT 0,
                                       last_updated TEXT,
                                       cached_at TEXT NOT NULL
                                   );

                                   CREATE TABLE ollama_models (
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
                                       cached_at TEXT NOT NULL,
                                       FOREIGN KEY (family_name) REFERENCES model_families(name) ON DELETE CASCADE
                                   );

                                   INSERT INTO model_families (name, description, pull_count, tag_count, cached_at)
                                   VALUES ('family-2', 'Test Family 2', 1, 1, '2024-01-01T00:00:00Z');

                                   INSERT INTO ollama_models (name, family_name, size, is_downloaded, cached_at)
                                   VALUES ('model-2', 'family-2', 200, 0, '2024-01-01T00:00:00Z');
                                   """;
            await setupCmd.ExecuteNonQueryAsync();
        }

        await InvokeMigrateSchemaAsync(conn);

        Assert.False(await HasColumnAsync(conn, "ollama_models", "download_status"));
        Assert.True(await HasColumnAsync(conn, "ollama_models", "is_downloaded"));
        Assert.True(await HasColumnAsync(conn, "ollama_models", "avg_token_per_sec"));

        await using var verifyCmd = conn.CreateCommand();
        verifyCmd.CommandText = "SELECT COUNT(*) FROM ollama_models WHERE name = 'model-2'";
        var rowCount = (long)(await verifyCmd.ExecuteScalarAsync() ?? 0L);
        Assert.Equal(1L, rowCount);
    }

    private static async Task InvokeMigrateSchemaAsync(SqliteConnection conn)
    {
        var method = typeof(DatabaseInitService).GetMethod(
            "MigrateSchemaAsync",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var task = method.Invoke(null, [conn]) as Task;
        Assert.NotNull(task);
        await task;
    }

    private static async Task<bool> HasColumnAsync(SqliteConnection conn, string tableName, string columnName)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{tableName}') WHERE name = @name";
        cmd.Parameters.AddWithValue("@name", columnName);
        var count = (long)(await cmd.ExecuteScalarAsync() ?? 0L);
        return count > 0;
    }
}
