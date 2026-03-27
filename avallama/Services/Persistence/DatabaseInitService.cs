// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace avallama.Services.Persistence;

public interface IDatabaseInitService
{
    Task<SqliteConnection> InitializeDatabaseAsync();
}

public class DatabaseInitService : IDatabaseInitService
{
    private readonly string _dbPath;

    // Canonical database schema, changing this will trigger a schema migration
    // This is not able to handle complex migrations, which would probably facilitate an "API breaking" change anyway
    private const string CanonicalSchema = """
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

                                           CREATE INDEX IF NOT EXISTS idx_models_pull_count_desc ON model_families(pull_count DESC);
                                           CREATE INDEX IF NOT EXISTS idx_models_name_asc ON ollama_models(name ASC);
                                           """;

    public DatabaseInitService(bool? isTest = false)
    {
        if (isTest.HasValue && isTest.Value)
        {
            _dbPath = ":memory:";
            return;
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var appDir = Path.Combine(appData, "avallama");
        if (!Directory.Exists(appDir))
            Directory.CreateDirectory(appDir);

        _dbPath = Path.Combine(appDir, "avallama.db");
    }

    public async Task<SqliteConnection> InitializeDatabaseAsync()
    {
        var isNew = !File.Exists(_dbPath);

        var connection = new SqliteConnection($"Data Source={_dbPath}");
        await connection.OpenAsync();

        if (isNew) await InitializeSchemaAsync(connection);

        var hasRequiredTables = await HasRequiredTablesAsync(connection);

        if (!hasRequiredTables)
        {
            // TODO: proper logging
            // Console.WriteLine("Database file is missing required tables. Reinitializing schema.");
            await InitializeSchemaAsync(connection);
            return connection;
        }

        var needsMigration = await NeedsMigrationAsync(connection);

        if (!needsMigration) return connection;
        // Console.WriteLine("Database schema is outdated. Migrating...");
        await MigrateSchemaAsync(connection);
        // Console.WriteLine("Database migration completed.");

        return connection;
    }

    private static async Task InitializeSchemaAsync(SqliteConnection conn)
    {
        var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
                           PRAGMA foreign_keys = ON;
                           PRAGMA journal_mode = WAL;
                           PRAGMA synchronous = NORMAL;
                           PRAGMA temp_store = MEMORY;
                           PRAGMA cache_size = 32000;

                           CREATE TABLE IF NOT EXISTS schema_metadata (
                           key TEXT PRIMARY KEY,
                           value TEXT NOT NULL
                           );

                           {CanonicalSchema}
                           """;
        await cmd.ExecuteNonQueryAsync();
        await StoreSchemaHashAsync(conn);
    }

    private static async Task<bool> NeedsMigrationAsync(SqliteConnection conn)
    {
        await using var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='schema_metadata'";
        var exists = await checkCmd.ExecuteScalarAsync();
        if (exists is null) return true;

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM schema_metadata WHERE key = 'schema_hash' LIMIT 1";
        var storedHash = await cmd.ExecuteScalarAsync() as string;
        var currentHash = ComputeSchemaHash();
        return storedHash != currentHash;
    }

    /// <summary>
    /// Method that migrates legacy database schemas to the current canonical schema.
    /// There are currently 2 migrations that this method can complete:
    /// Migration of the pre-v0.3.0 "download_status" column to "is_downloaded", and
    /// migration of the pre-v0.3.2 "ollama_models" table to add a new "avg_tokens_per_sec" column.
    /// </summary>
    /// <param name="conn">The database connection</param>
    /// <exception cref="InvalidOperationException">Thrown when schema migration fails.</exception>
    private static async Task MigrateSchemaAsync(SqliteConnection conn)
    {
        await using var transaction = await conn.BeginTransactionAsync();
        try
        {
            var hasLegacyDownloadColumn = await HasColumnAsync(conn, "ollama_models", "download_status");

            await using var metaCmd = conn.CreateCommand();
            metaCmd.CommandText = """
                                  CREATE TABLE IF NOT EXISTS schema_metadata (
                                  key TEXT PRIMARY KEY,
                                  value TEXT NOT NULL
                                  );
                                  """;
            await metaCmd.ExecuteNonQueryAsync();

            // Migrate legacy pre-v0.3.0 table structure (download_status -> is_downloaded)
            if (hasLegacyDownloadColumn)
            {
                await using var migrationCmd = conn.CreateCommand();
                migrationCmd.CommandText = """
                                               PRAGMA foreign_keys=OFF;

                                               CREATE TABLE ollama_models_new (
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
                                                   is_downloaded INTEGER NOT NULL DEFAULT 0, -- new column from v0.3.0
                                                   avg_token_per_sec REAL, -- new column from v0.3.2
                                                   cached_at TEXT NOT NULL,
                                                   FOREIGN KEY (family_name) REFERENCES model_families(name) ON DELETE CASCADE
                                               );

                                               -- insert old data to a new table
                                               INSERT INTO ollama_models_new (
                                                   name, family_name, parameters, size, format, quantization, architecture,
                                                   block_count, context_length, embedding_length, additional_info,
                                                   is_downloaded, cached_at
                                               )
                                               SELECT
                                                   name, family_name, parameters, size, format, quantization, architecture,
                                                   block_count, context_length, embedding_length, additional_info,
                                                   CASE WHEN download_status = 5 THEN 1 ELSE 0 END, -- conversion to new column
                                                   cached_at
                                               FROM ollama_models;

                                               -- drop old table
                                               DROP TABLE ollama_models;

                                               -- rename new table
                                               ALTER TABLE ollama_models_new RENAME TO ollama_models;

                                               -- restore indexes
                                               CREATE INDEX IF NOT EXISTS idx_models_name_asc ON ollama_models(name ASC);
                                               CREATE INDEX IF NOT EXISTS idx_models_pull_count_desc ON model_families(pull_count DESC);

                                               PRAGMA foreign_keys=ON;
                                           """;
                await migrationCmd.ExecuteNonQueryAsync();
            }

            // Add new avg_token_per_sec column to databases pre-v0.3.2
            var hasAvgTokenPerSecColumn = await HasColumnAsync(conn, "ollama_models", "avg_token_per_sec");
            if (!hasAvgTokenPerSecColumn)
            {
                await using var addTpsColumnCmd = conn.CreateCommand();
                addTpsColumnCmd.CommandText = "ALTER TABLE ollama_models ADD COLUMN avg_token_per_sec REAL";
                await addTpsColumnCmd.ExecuteNonQueryAsync();
            }

            if (!hasLegacyDownloadColumn && hasAvgTokenPerSecColumn)
            {
                await using var schemaCmd = conn.CreateCommand();
                schemaCmd.CommandText = CanonicalSchema;
                await schemaCmd.ExecuteNonQueryAsync();
            }

            await StoreSchemaHashAsync(conn);

            await transaction.CommitAsync();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            throw new InvalidOperationException("Schema migration failed.", ex);
        }
    }

    private static async Task StoreSchemaHashAsync(SqliteConnection conn)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO schema_metadata (key, value) VALUES ('schema_hash', @Hash)";
        cmd.Parameters.AddWithValue("@Hash", ComputeSchemaHash());
        await cmd.ExecuteNonQueryAsync();
    }

    private static string ComputeSchemaHash()
    {
        var normalized = NormalizeSchema(CanonicalSchema);
        var hashBytes = SHA512.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hashBytes);
    }

    private static string NormalizeSchema(string schema)
    {
        return string.Join(" ", schema.Split([' ', '\r', '\n', '\t'],
            StringSplitOptions.RemoveEmptyEntries));
    }

    private static async Task<bool> HasRequiredTablesAsync(SqliteConnection connection)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
                          SELECT COUNT(*) FROM sqlite_master
                          WHERE type='table' AND name IN ('conversations', 'messages');
                          """;
        var count = (long)(await cmd.ExecuteScalarAsync() ?? 0L);
        return count == 2;
    }

    private static async Task<bool> HasColumnAsync(SqliteConnection connection, string tableName, string columnName)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{tableName}') WHERE name=@ColumnName";
        cmd.Parameters.AddWithValue("@ColumnName", columnName);
        var count = (long)(await cmd.ExecuteScalarAsync() ?? 0L);
        return count > 0;
    }
}
