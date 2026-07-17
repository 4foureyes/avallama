// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using avallama.Constants.Keys;
using avallama.Constants.States;
using avallama.Models;
using avallama.Models.Dtos;
using avallama.Models.Ollama;
using avallama.Services.Persistence;

namespace avallama.Services.Ollama;

public delegate void OllamaServiceStatusChangedHandler(OllamaServiceStatus serviceStatus);

/// <summary>
/// Defines a high-level service for interacting with the Ollama ecosystem.
/// This acts as a facade, aggregating process management, API communication, and web scraping capabilities.
/// </summary>
public interface IOllamaService
{
    #region Interface

    /// <summary>
    /// Gets the unified current status of Ollama.
    /// </summary>
    OllamaServiceStatus CurrentServiceStatus { get; }

    /// <summary>
    /// Event raised when the unified status of Ollama changes.
    /// </summary>
    event OllamaServiceStatusChangedHandler? StatusChanged;

    /// <summary>
    /// Starts the local Ollama process asynchronously.
    /// </summary>
    Task StartProcessAsync();

    /// <summary>
    /// Stops the local Ollama process asynchronously.
    /// </summary>
    Task StopProcessAsync();

    /// <summary>
    /// Manually triggers a check for the Ollama API connection status.
    /// </summary>
    Task CheckConnectionAsync();

    /// <summary>
    /// Attempts to reconnect to the Ollama API.
    /// </summary>
    Task RetryConnectionAsync();

    /// <summary>
    /// Retrieves a list of models currently downloaded via the API.
    /// </summary>
    /// <returns>A list of downloaded Ollama models.</returns>
    Task<IList<OllamaModel>> GetDownloadedModelsAsync();

    /// <summary>
    /// Deletes a specified model from the library via the API.
    /// </summary>
    /// <param name="modelName">The name of the model to delete.</param>
    /// <returns>True if deletion was successful; otherwise, false.</returns>
    Task<bool> DeleteModelAsync(string modelName);

    /// <summary>
    /// Enriches a model object with detailed metadata from the API (/api/tags, /api/show).
    /// </summary>
    /// <param name="model">The model to enrich.</param>
    Task EnrichModelAsync(OllamaModel model);

    /// <summary>
    /// Downloads (pulls) a specific model from the Ollama library.
    /// </summary>
    /// <param name="modelName">The name of the model to download.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An async stream of download progress updates.</returns>
    IAsyncEnumerable<DownloadResponse> DownloadModelAsync(string modelName, CancellationToken ct = default);

    /// <summary>
    /// Generates a response from a model based on a conversation history.
    /// </summary>
    /// <param name="messageHistory">The list of previous messages in the conversation.</param>
    /// <param name="modelName">The model to use for generation.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An async stream of response chunks.</returns>
    IAsyncEnumerable<OllamaResponse> GenerateMessageAsync(List<Message> messageHistory, string modelName, CancellationToken ct = default);

    /// <summary>
    /// Retrieves model families fetched during the last scraping session.
    /// </summary>
    /// <returns>A list of model families.</returns>
    Task<IList<OllamaModelFamily>> GetScrapedFamiliesAsync();

    /// <summary>
    /// Scrapes and streams all available models from the Ollama online library.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async stream of discovered models.</returns>
    IAsyncEnumerable<OllamaModel> StreamAllScrapedModelsAsync(CancellationToken cancellationToken);

    #endregion
}

/// <summary>
/// Implementation of the IOllamaService facade, orchestrating process management, API calls, and scraping.
/// </summary>
internal class OllamaService : IOllamaService
{
    #region Fields & Properties

    public const string DownloadUrl = @"https://ollama.com/download/";

    private IOllamaProcessManager _processManager;
    private IOllamaApiClient _apiClient;
    private IOllamaScraper _scraper;
    private IConfigurationService _configurationService;

    private OllamaScraperResult? _currentScrapeSession;

    #endregion

    #region Constructor

    public OllamaService(
        IOllamaProcessManager processManager,
        IOllamaApiClient apiClient,
        IOllamaScraper scraper,
        IConfigurationService configurationService)
    {
        _processManager = processManager;
        _apiClient = apiClient;
        _scraper = scraper;
        _configurationService = configurationService;

        _processManager.StatusChanged += _ => EvaluateServiceStatus();
        _apiClient.StatusChanged += _ => EvaluateServiceStatus();
    }

    #endregion

    #region Events & Status

    public event OllamaServiceStatusChangedHandler? StatusChanged;

    public OllamaServiceStatus CurrentServiceStatus
    {
        get;
        private set
        {
            if (field.ServiceState == value.ServiceState && field.Message == value.Message) return;
            field = value;
            StatusChanged?.Invoke(value);
        }
    } = new(OllamaServiceState.Stopped);

    #endregion

    #region Process Management

    public async Task StartProcessAsync() => await _processManager.StartAsync();
    public async Task StopProcessAsync() => await _processManager.StopAsync();

    #endregion

    #region API

    public async Task CheckConnectionAsync() => await _apiClient.CheckConnectionAsync();

    public async Task RetryConnectionAsync()
    {
        var isRemote = OllamaApiClient.IsConnectionRemote(_configurationService.ReadSetting(ConfigurationKey.ApiHost));

        if (!isRemote)
        {
            await StartProcessAsync();
        }

        await _apiClient.RetryConnectionAsync();
    }

    public async Task EnrichModelAsync(OllamaModel model) => await _apiClient.EnrichModelAsync(model);
    public async Task<IList<OllamaModel>> GetDownloadedModelsAsync() => await _apiClient.GetDownloadedModelsAsync();
    public async Task<bool> DeleteModelAsync(string modelName) => await _apiClient.DeleteModelAsync(modelName);

    public async IAsyncEnumerable<DownloadResponse> DownloadModelAsync(
        string modelName,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var response in _apiClient.PullModelAsync(modelName, ct))
        {
            yield return response;
        }
    }

    public async IAsyncEnumerable<OllamaResponse> GenerateMessageAsync(
        List<Message> messageHistory,
        string modelName,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var response in _apiClient.GenerateMessageAsync(messageHistory, modelName, ct))
        {
            yield return response;
        }
    }

    #endregion

    #region Scraper

    public Task<IList<OllamaModelFamily>> GetScrapedFamiliesAsync()
    {
        // Return families from the cached scrape session if available, then clear the session cache.
        if (_currentScrapeSession?.Families is not { } families)
            return Task.FromResult<IList<OllamaModelFamily>>([]);

        _currentScrapeSession = null;
        return Task.FromResult(families);
    }

    public async IAsyncEnumerable<OllamaModel> StreamAllScrapedModelsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        _currentScrapeSession = null;
        var result = await _scraper.GetAllOllamaModelsAsync(cancellationToken);
        _currentScrapeSession = result;

        await foreach (var model in result.Models.WithCancellation(cancellationToken))
        {
            yield return model;
        }
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Aggregates the internal Process and API statuses into a single public state.
    /// </summary>
    private void EvaluateServiceStatus()
    {
        var isRemote = OllamaApiClient.IsConnectionRemote(_configurationService.ReadSetting(ConfigurationKey.ApiHost));

        OllamaServiceState newServiceState;
        string? message = null;

        if (isRemote)
        {
            // if it's remote we only check for the API state
            switch (_apiClient.Status.ApiState)
            {
                case OllamaApiState.Connecting:
                case OllamaApiState.Reconnecting:
                    newServiceState = OllamaServiceState.Starting;
                    break;

                case OllamaApiState.Connected:
                    newServiceState = OllamaServiceState.Ready;
                    break;

                case OllamaApiState.Failed:
                    newServiceState = OllamaServiceState.Failed;
                    message = _apiClient.Status.Message;
                    break;

                case OllamaApiState.Disconnected:
                default:
                    newServiceState = OllamaServiceState.Stopped;
                    break;
            }
        }
        else
        {
            // if it's local we check for both the Process and API states
            var procState = _processManager.Status.ProcessState;
            var apiState = _apiClient.Status.ApiState;

            switch (procState)
            {
                case OllamaProcessState.NotInstalled:
                    newServiceState = OllamaServiceState.NotInstalled;
                    message = _processManager.Status.Message;
                    break;

                case OllamaProcessState.Failed:
                    newServiceState = OllamaServiceState.Failed;
                    message = _processManager.Status.Message;
                    break;

                case OllamaProcessState.Stopped:
                    newServiceState = OllamaServiceState.Stopped;
                    break;

                case OllamaProcessState.Starting:
                case OllamaProcessState.Running when apiState is OllamaApiState.Connecting or OllamaApiState.Reconnecting:
                    // if the process starts, or the process is already running but the client is still connecting to API
                    newServiceState = OllamaServiceState.Starting;
                    break;

                case OllamaProcessState.Running when apiState == OllamaApiState.Connected:
                    newServiceState = OllamaServiceState.Ready;
                    break;

                case OllamaProcessState.Running when apiState == OllamaApiState.Failed:
                    newServiceState = OllamaServiceState.Failed;
                    message = _apiClient.Status.Message;
                    break;

                default:
                    newServiceState = OllamaServiceState.Stopped;
                    break;
            }
        }

        CurrentServiceStatus = new OllamaServiceStatus(newServiceState, message);
    }

    #endregion
}
