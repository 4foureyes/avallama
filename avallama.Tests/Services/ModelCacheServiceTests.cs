using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using avallama.Models;
using avallama.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace avallama.Tests.Services;

[Collection("Database Tests")]
public class ModelCacheServiceTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ModelCacheService _modelCacheService;

    public ModelCacheServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        InitializeTestSchema(_connection);

        _modelCacheService = new ModelCacheService(_connection);
    }

    private static void InitializeTestSchema(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """

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
                          parameters REAL,
                          size INTEGER NOT NULL,
                          format TEXT,
                          quantization TEXT,
                          architecture TEXT,
                          block_count INTEGER,
                          context_length INTEGER,
                          embedding_length INTEGER,
                          additional_info TEXT,
                          download_status INTEGER NOT NULL DEFAULT 0,
                          cached_at TEXT NOT NULL,
                          FOREIGN KEY (family_name) REFERENCES model_families(name) ON DELETE CASCADE
                          );
                          """;
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public async Task CacheOllamaModelFamilyAsync_WithNothingInDb_InsertsModelFamilies()
    {
        var modelFamilies = new List<OllamaModelFamily>
        {
            new()
            {
                Name = "model-family-1",
                Description = "Test Model Family 1",
                PullCount = 100,
                Labels = new List<string> { "tag1", "tag2" },
                TagCount = 2,
                LastUpdated = DateTime.UtcNow
            },
            new()
            {
                Name = "model-family-2",
                Description = "Test Model Family 2",
                PullCount = 200,
                Labels = new List<string> { "tag3", "tag4" },
                TagCount = 2,
                LastUpdated = DateTime.UtcNow
            }
        };

        await _modelCacheService.CacheModelFamilyAsync(modelFamilies);

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM model_families";
        var count = (long)(await cmd.ExecuteScalarAsync() ?? 0);

        Assert.Equal(2, count);
    }

    [Fact]
    public async Task CacheOllamaModelFamilyAsync_WithExistingDb_UpdatesModelFamilies()
    {
        var initialModelFamilies = new List<OllamaModelFamily>
        {
            new()
            {
                Name = "model-family-1",
                Description = "Initial Description",
                PullCount = 100,
                Labels = new List<string> { "tag1" },
                TagCount = 1,
                LastUpdated = DateTime.UtcNow.AddDays(-1)
            }
        };

        await _modelCacheService.CacheModelFamilyAsync(initialModelFamilies);

        var updatedModelFamilies = new List<OllamaModelFamily>
        {
            new()
            {
                Name = "model-family-1",
                Description = "Updated Description",
                PullCount = 150,
                Labels = new List<string> { "tag1", "tag2" },
                TagCount = 2,
                LastUpdated = DateTime.UtcNow
            }
        };

        await _modelCacheService.CacheModelFamilyAsync(updatedModelFamilies);

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText =
            "SELECT description, pull_count, labels, tag_count FROM model_families WHERE name = 'model-family-1'";
        await using var reader = await cmd.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Equal("Updated Description", reader.GetString(0));
        Assert.Equal(150, reader.GetInt32(1));
        var labels = reader.GetString(2);
        Assert.Contains("tag1", labels);
        Assert.Contains("tag2", labels);
        Assert.Equal(2, reader.GetInt32(3));
    }

    [Fact]
    public async Task CacheOllamaModelFamilyAsync_WithDelete_RemovesModelFamilies()
    {
        var initialModelFamilies = new List<OllamaModelFamily>
        {
            new()
            {
                Name = "model-family-1",
                Description = "Test Model Family 1",
                PullCount = 100,
                Labels = new List<string> { "tag1", "tag2" },
                TagCount = 2,
                LastUpdated = DateTime.UtcNow
            },
            new()
            {
                Name = "model-family-2",
                Description = "Test Model Family 2",
                PullCount = 200,
                Labels = new List<string> { "tag3", "tag4" },
                TagCount = 2,
                LastUpdated = DateTime.UtcNow
            }
        };
        await _modelCacheService.CacheModelFamilyAsync(initialModelFamilies);

        var updatedModelFamilies = new List<OllamaModelFamily>
        {
            new()
            {
                Name = "model-family-1",
                Description = "Test Model Family 1",
                PullCount = 100,
                Labels = new List<string> { "tag1", "tag2" },
                TagCount = 2,
                LastUpdated = DateTime.UtcNow
            }
        };
        await _modelCacheService.CacheModelFamilyAsync(updatedModelFamilies);
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM model_families";
        var count = (long)(await cmd.ExecuteScalarAsync() ?? 0);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task CacheOllamaModelAsync_WithNothingInDb_InsertsModels()
    {
        var modelFamily = new OllamaModelFamily
        {
            Name = "model-1",
            Description = "Test Model Family 1",
            PullCount = 100,
            Labels = new List<string> { "tag1", "tag2" },
            TagCount = 2,
            LastUpdated = DateTime.UtcNow
        };

        await _modelCacheService.CacheModelFamilyAsync(new List<OllamaModelFamily> { modelFamily });

        var models = new List<OllamaModel>
        {
            new()
            {
                Name = "model-1:7b",
                Parameters = 1_010_000_000,
                DownloadStatus = ModelDownloadStatus.Downloaded,
                RunsSlow = false,
                Info = new Dictionary<string, string>
                {
                    { "quantization", "MPFX6" },
                    { "format", "Test Format" }
                },
                Size = 2048000
            }
        };

        await _modelCacheService.CacheModelsAsync(models);

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM ollama_models";
        var count = (long)(await cmd.ExecuteScalarAsync() ?? 0);

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task CacheOllamaModelAsync_WithExistingDb_UpdatesModels()
    {
        var modelFamily = new OllamaModelFamily
        {
            Name = "model-1",
            Description = "Test Model Family 1",
            PullCount = 100,
            Labels = new List<string> { "tag1", "tag2" },
            TagCount = 2,
            LastUpdated = DateTime.UtcNow
        };

        await _modelCacheService.CacheModelFamilyAsync(new List<OllamaModelFamily> { modelFamily });

        var initialModel = new List<OllamaModel>
        {
            new()
            {
                Name = "model-1:7b",
                Parameters = 1_010_000_000,
                DownloadStatus = ModelDownloadStatus.Downloading,
                RunsSlow = false,
                Info = new Dictionary<string, string>
                {
                    { "quantization", "MPFX6" },
                    { "format", "Test Format" }
                },
                Size = 2048000
            }
        };
        await _modelCacheService.CacheModelsAsync(initialModel);
        var updatedModel = new List<OllamaModel>
        {
            new()
            {
                Name = "model-1:7b",
                Parameters = 1_010_000_000,
                DownloadStatus = ModelDownloadStatus.Downloaded,
                RunsSlow = true,
                Info = new Dictionary<string, string>
                {
                    { "quantization", "MPFX6" },
                    { "format", "Updated Format" }
                },
                Size = 4096000
            }
        };
        await _modelCacheService.CacheModelsAsync(updatedModel);
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText =
            "SELECT quantization, parameters, format, download_status FROM ollama_models WHERE name = 'model-1:7b'";
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("MPFX6", reader.GetString(0));
        Assert.Equal(1_010_000_000, reader.GetInt64(1));
        Assert.Equal("Updated Format", reader.GetString(2));
        Assert.Equal((int)ModelDownloadStatus.Downloaded, reader.GetInt32(3));
    }

    [Fact]
    public async Task CacheOllamaModelAsync_WithDelete_RemovesModels()
    {
        var modelFamily = new OllamaModelFamily
        {
            Name = "model-1",
            Description = "Test Model Family 1",
            PullCount = 100,
            Labels = new List<string> { "tag1", "tag2" },
            TagCount = 2,
            LastUpdated = DateTime.UtcNow
        };

        await _modelCacheService.CacheModelFamilyAsync(new List<OllamaModelFamily> { modelFamily });
        var initialModels = new List<OllamaModel>
        {
            new()
            {
                Name = "model-1:7b",
                Parameters = 1_010_000_000,
                DownloadStatus = ModelDownloadStatus.Downloaded,
                RunsSlow = false,
                Info = new Dictionary<string, string>
                {
                    { "quantization", "MPFX6" },
                    { "format", "Test Format" }
                },
                Size = 4096000
            },
            new()
            {
                Name = "model-1:13b",
                Parameters = 1_010_000_000,
                DownloadStatus = ModelDownloadStatus.Downloaded,
                RunsSlow = false,
                Info = new Dictionary<string, string>
                {
                    { "quantization", "MPFX6" },
                    { "format", "Test Format" }
                },
                Size = 4096000
            }
        };
        await _modelCacheService.CacheModelsAsync(initialModels);
        var updatedModels = new List<OllamaModel>
        {
            new()
            {
                Name = "model-1:7b",
                Parameters = 1_010_000_000,
                DownloadStatus = ModelDownloadStatus.Downloaded,
                RunsSlow = true,
                Info = new Dictionary<string, string>
                {
                    { "quantization", "MPFX6" },
                    { "format", "Test Format" }
                },
                Size = 2048000
            }
        };
        await _modelCacheService.CacheModelsAsync(updatedModels);
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM ollama_models";
        var count = (long)(await cmd.ExecuteScalarAsync() ?? 0);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task UpdateOllamaModel_UpdatesModel()
    {
        var modelFamily = new OllamaModelFamily
        {
            Name = "model-1",
            Description = "Test Model Family 1",
            PullCount = 100,
            Labels = new List<string> { "tag1", "tag2" },
            TagCount = 2,
            LastUpdated = DateTime.UtcNow
        };

        await _modelCacheService.CacheModelFamilyAsync(new List<OllamaModelFamily> { modelFamily });

        var initialModel = new List<OllamaModel>
        {
            new()
            {
                Name = "model-1:7b",
                Parameters = 1_010_000_000,
                DownloadStatus = ModelDownloadStatus.Downloaded,
                RunsSlow = false,
                Info = new Dictionary<string, string>
                {
                    { "quantization", "MPFX6" },
                    { "format", "Test Format" }
                },
                Size = 2048000
            }
        };
        await _modelCacheService.CacheModelsAsync(initialModel);

        var info = new Dictionary<string, string>
        {
            { "quantization", "MPFX6" },
            { "format", "Test Format" },
            { "architecture", "llama" },
            { "context_length", "8192" },
            { "block_count", "32" },
            { "embedding_length", "4096" }
        };

        var modelToUpdate = new OllamaModel
        {
            Name = "model-1:7b",
            Parameters = 1_010_000_000,
            Info = info,
            Size = 2048,
            DownloadStatus = ModelDownloadStatus.Downloaded,
            DownloadProgress = 1.0,
            RunsSlow = false
        };

        await _modelCacheService.UpdateModelAsync(modelToUpdate);

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText =
            "SELECT architecture, context_length, block_count, embedding_length, download_status FROM ollama_models WHERE name = 'model-1:7b'";
        await using var reader = await cmd.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Equal("llama", reader.GetString(0));
        Assert.Equal(8192, reader.GetInt32(1));
        Assert.Equal(32, reader.GetInt32(2));
        Assert.Equal(4096, reader.GetInt32(3));
        Assert.Equal((int)ModelDownloadStatus.Downloaded, reader.GetInt32(4));
    }

    [Fact]
    public async Task GetCachedOllamaModelFamiliesAsync_ReturnsCachedFamilies()
    {
        var modelFamilies = new List<OllamaModelFamily>
        {
            new()
            {
                Name = "model-family-1",
                Description = "Test Model Family 1",
                PullCount = 100,
                Labels = new List<string> { "tag1", "tag2" },
                TagCount = 2,
                LastUpdated = DateTime.UtcNow
            },
            new()
            {
                Name = "model-family-2",
                Description = "Test Model Family 2",
                PullCount = 200,
                Labels = new List<string> { "tag3", "tag4" },
                TagCount = 2,
                LastUpdated = DateTime.UtcNow
            }
        };

        await _modelCacheService.CacheModelFamilyAsync(modelFamilies);

        var cachedFamilies = await _modelCacheService.GetCachedModelFamiliesAsync();

        Assert.Equal(2, cachedFamilies.Count);
        Assert.Contains(cachedFamilies, mf => mf.Name == "model-family-1");
        Assert.Contains(cachedFamilies, mf => mf.Name == "model-family-2");
    }

    [Fact]
    public async Task GetCachedOllamaModelsAsync_ReturnsCachedModels()
    {
        var modelFamily = new OllamaModelFamily
        {
            Name = "model-1",
            Description = "Test Model Family 1",
            PullCount = 100,
            Labels = new List<string> { "tag1", "tag2" },
            TagCount = 2,
            LastUpdated = DateTime.UtcNow
        };

        await _modelCacheService.CacheModelFamilyAsync(new List<OllamaModelFamily> { modelFamily });

        var models = new List<OllamaModel>
        {
            new()
            {
                Name = "model-1:7b",
                Parameters = 1_010_000_000,
                DownloadStatus = ModelDownloadStatus.Downloaded,
                RunsSlow = false,
                Info = new Dictionary<string, string>
                {
                    { "quantization", "MPFX6" },
                    { "format", "Test Format" }
                },
                Size = 2048000
            },
            new()
            {
                Name = "model-1:13b",
                Parameters = 1_010_000_000,
                DownloadStatus = ModelDownloadStatus.Downloaded,
                RunsSlow = false,
                Info = new Dictionary<string, string>
                {
                    { "quantization", "MPFX6" },
                    { "format", "Test Format" }
                },
                Size = 4096000
            }
        };

        await _modelCacheService.CacheModelsAsync(models);

        var cachedModels = await _modelCacheService.GetCachedModelsAsync();

        Assert.Equal(2, cachedModels.Count);
        Assert.Contains(cachedModels, m => m.Name == "model-1:7b");
        Assert.Contains(cachedModels, m => m.Name == "model-1:13b");
    }

    // Integration tests, ModelCacheService + DatabaseLock

    [Fact]
    public async Task ConcurrentReads_DoNotBlock()
    {
        var modelFamilies = new List<OllamaModelFamily>
        {
            new()
            {
                Name = "model-family-1",
                Description = "Test",
                PullCount = 100,
                Labels = new List<string> { "tag1" },
                TagCount = 1,
                LastUpdated = DateTime.UtcNow
            }
        };
        await _modelCacheService.CacheModelFamilyAsync(modelFamilies);

        var tasks = Enumerable.Range(0, 50)
            .Select(_ => Task.Run(async () => await _modelCacheService.GetCachedModelFamiliesAsync()))
            .ToArray();

        await Task.WhenAll(tasks);

        Assert.All(tasks, t => Assert.True(t.IsCompletedSuccessfully));
        Assert.All(tasks, t => Assert.Single((IEnumerable)t.Result));
    }

    [Fact]
    public async Task MixedReadWriteOperations_AreThreadSafe()
    {
        var initialFamily = new OllamaModelFamily
        {
            Name = "model-1",
            Description = "Initial",
            PullCount = 100,
            Labels = new List<string> { "tag1" },
            TagCount = 1,
            LastUpdated = DateTime.UtcNow
        };

        await _modelCacheService.CacheModelFamilyAsync(new List<OllamaModelFamily> { initialFamily });

        var readTasks = Enumerable.Range(0, 30)
            .Select(_ => Task.Run(async () => await _modelCacheService.GetCachedModelFamiliesAsync()));

        var writeTasks = Enumerable.Range(0, 20)
            .Select(i => Task.Run(async () =>
            {
                var updated = new OllamaModelFamily
                {
                    Name = "model-1",
                    Description = "$Updated {i}",
                    PullCount = 100 + i,
                    Labels = new List<string> { "tag1" },
                    TagCount = 1,
                    LastUpdated = DateTime.UtcNow
                };
                await _modelCacheService.CacheModelFamilyAsync(new List<OllamaModelFamily> { updated });
            }));

        await Task.WhenAll(readTasks.Concat(writeTasks));

        var final = await _modelCacheService.GetCachedModelFamiliesAsync();
        Assert.Single(final);
    }

    [Fact]
    public async Task HighVolumeUpdates_MaintainConsistency()
    {
        var modelFamily = new OllamaModelFamily
        {
            Name = "model-1",
            Description = "Test",
            PullCount = 100,
            Labels = new List<string> { "tag1" },
            TagCount = 1,
            LastUpdated = DateTime.UtcNow
        };

        await _modelCacheService.CacheModelFamilyAsync(new List<OllamaModelFamily> { modelFamily });

        var model = new OllamaModel
        {
            Name = "model-1:7b",
            Parameters = 1_000_000_000,
            DownloadStatus = ModelDownloadStatus.Downloaded,
            RunsSlow = false,
            Info = new Dictionary<string, string>
            {
                { "quantization", "MPFX6" },
                { "format", "gguf" }
            },
            Size = 2048000
        };

        await _modelCacheService.CacheModelsAsync(new List<OllamaModel> { model });

        var tasks = Enumerable.Range(0, 100)
            .Select(i => Task.Run(async () =>
            {
                var updated = new OllamaModel
                {
                    Name = "model-1:7b",
                    Parameters = 1_000_000_000 + i,
                    Info = new Dictionary<string, string>
                    {
                        ["iteration"] = i.ToString(),
                        ["format"] = "gguf",
                        ["quantization"] = $"MPFX{i}"
                    },
                    Size = 2048 + i,
                    DownloadStatus = ModelDownloadStatus.Downloaded,
                    DownloadProgress = 1.0,
                    RunsSlow = false
                };
                await _modelCacheService.UpdateModelAsync(updated);
            }))
            .ToArray();

        await Task.WhenAll(tasks);

        var cached = await _modelCacheService.GetCachedModelsAsync();
        Assert.Single(cached);
        Assert.Equal("model-1:7b", cached[0].Name);
    }

    [Fact]
    public async Task ConcurrentReadsDuringBulkWrite_Succeed()
    {
        var modelFamilies = Enumerable.Range(0, 100)
            .Select(i => new OllamaModelFamily
            {
                Name = $"family-{i}",
                Description = $"Desc {i}",
                PullCount = i,
                Labels = new List<string> { $"tag{i}" },
                TagCount = 1,
                LastUpdated = DateTime.UtcNow
            }).ToList();

        var writeTask = Task.Run(async () =>
            await _modelCacheService.CacheModelFamilyAsync(modelFamilies));

        var readTasks = Enumerable.Range(0, 50)
            .Select(_ => Task.Run(async () => await _modelCacheService.GetCachedModelFamiliesAsync()))
            .ToArray();

        await Task.WhenAll(readTasks.Append(writeTask));

        var final = await _modelCacheService.GetCachedModelFamiliesAsync();
        Assert.Equal(100, final.Count);
    }

    // Stress tests

    [Fact]
    public async Task CacheOllamaModelFamilyAsync_StressTest_InsertsThousandFamilies()
    {
        const int familyCount = 1000;

        var modelFamilies = CreateModelFamilies(familyCount);

        var sw = Stopwatch.StartNew();
        await _modelCacheService.CacheModelFamilyAsync(modelFamilies);
        sw.Stop();

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM model_families";
        var count = (long)(await cmd.ExecuteScalarAsync() ?? 0);

        Assert.Equal(familyCount, count);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"Stress test took too long: {sw.Elapsed}");
    }

    [Fact]
    public async Task CacheOllamaModelFamilyAndModels_StressTest_InsertsFifteenThousandEach()
    {
        // trying to future-proof here
        const int familyCount = 15000;
        const int modelCount = 15000;

        var families = CreateModelFamilies(familyCount);

        var sw = Stopwatch.StartNew();
        await _modelCacheService.CacheModelFamilyAsync(families);

        var models = CreateModelsForFamilies(families, modelCount);
        await _modelCacheService.CacheModelsAsync(models);
        sw.Stop();

        await using var cmdFamilies = _connection.CreateCommand();
        cmdFamilies.CommandText = "SELECT COUNT(*) FROM model_families";
        var familyRowCount = (long)(await cmdFamilies.ExecuteScalarAsync() ?? 0);

        await using var cmdModels = _connection.CreateCommand();
        cmdModels.CommandText = "SELECT COUNT(*) FROM ollama_models";
        var modelRowCount = (long)(await cmdModels.ExecuteScalarAsync() ?? 0);

        Assert.Equal(familyCount, familyRowCount);
        Assert.Equal(modelCount, modelRowCount);

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"High volume stress test took too long: {sw.Elapsed}");
    }

    private static List<OllamaModelFamily> CreateModelFamilies(int count)
    {
        var now = DateTime.UtcNow;
        var list = new List<OllamaModelFamily>(count);

        for (var i = 0; i < count; i++)
        {
            list.Add(new OllamaModelFamily
            {
                Name = $"stress-family-{i}",
                Description = $"Stress Test Model Family {i}",
                PullCount = i,
                Labels = new List<string> { $"tag-{i}", $"tag-{i + 1}" },
                TagCount = 2,
                LastUpdated = now
            });
        }

        return list;
    }

    private static List<OllamaModel> CreateModelsForFamilies(
        IReadOnlyList<OllamaModelFamily> families,
        int count)
    {
        var list = new List<OllamaModel>(count);
        var max = Math.Min(count, families.Count);

        for (var i = 0; i < max; i++)
        {
            var family = families[i];
            list.Add(new OllamaModel
            {
                Name = $"{family.Name}:model-{i}",
                Parameters = 1_000_000_000 + i,
                DownloadStatus = ModelDownloadStatus.Downloaded,
                RunsSlow = false,
                Info = new Dictionary<string, string>
                {
                    { "quantization", $"Q{i % 8}" },
                    { "format", "gguf" }
                },
                Size = 1024L * (i + 1)
            });
        }

        return list;
    }

    public async ValueTask DisposeAsync()
    {
        await Task.Delay(100);

        await _connection.CloseAsync();
        await _connection.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
