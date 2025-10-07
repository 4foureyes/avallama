// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using avallama.Constants;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using avallama.Dtos;
using avallama.Models;
using avallama.Utilities;

namespace avallama.Services;

// A delegate where we provide the method definition that subscribers must conform to
public delegate void ServiceStatusChangedHandler(ServiceStatus? status, string? message);

public interface IOllamaService
{
    Task Start();
    void Stop();
    Task Retry();
    Task<bool> IsModelDownloaded();
    Task<string> GetModelParamNum(string modelName);
    IAsyncEnumerable<DownloadResponse> PullModel(string modelName);
    IAsyncEnumerable<OllamaResponse> GenerateMessage(List<Message> messageHistory, string modelName);
    Task<bool> IsOllamaServerRunning();
    Task<List<OllamaModel>> ListLibraryModelsAsOllamaModelsAsync();
    Task<bool> DeleteModel(string modelName);
    Task<ObservableCollection<OllamaModel>> ListDownloaded();
    ServiceStatus? CurrentServiceStatus { get; }
    string? CurrentServiceMessage { get; }
    event ServiceStatusChangedHandler? ServiceStatusChanged;
}

public class OllamaService : IOllamaService
{
    public const int DefaultApiPort = 11434;
    public const string DefaultApiHost = "localhost";

    private readonly IConfigurationService _configurationService;
    private readonly IDialogService _dialogService;

    // Maximum wait time for requests, currently 15 seconds for slower machines
    // but might need to be readjusted in the future
    private readonly TimeSpan _timeout = TimeSpan.FromSeconds(15);

    private Process? _ollamaProcess;
    private HttpClient _httpClient;
    private readonly IAvaloniaDispatcher _dispatcher;
    private string? _apiHost = DefaultApiHost;
    private string? _apiPort = DefaultApiPort.ToString();
    private string OllamaPath { get; set; } = "";
    private bool _started;

    public ServiceStatus? CurrentServiceStatus { get; private set; }
    public string? CurrentServiceMessage { get; private set; }

    private string ApiBaseUrl => $"http://{_apiHost}:{_apiPort}";

    // Delegate for subscribers (MainViewModel subscribes here)
    public event ServiceStatusChangedHandler? ServiceStatusChanged;

    // Delegate for the start process function to allow mocking in tests
    public Func<ProcessStartInfo, Process?> StartProcessFunc { get; init; } = Process.Start;

    // Delegate for getting ollama processes
    // This is needed because for some godforsaken reason the method started ollama on my local during testing
    // so we mock it
    public Func<int> GetProcessCountFunc { get; init; } = () => Process.GetProcessesByName("ollama").Length;

    public OllamaService(IConfigurationService configurationService, IDialogService dialogService,
        IAvaloniaDispatcher dispatcher)
    {
        _configurationService = configurationService;
        _dialogService = dialogService;
        _dispatcher = dispatcher;
        _httpClient = new HttpClient();
        LoadSettings();
    }

    // This method is called during every API call so the app has up-to-date settings in memory
    // without having to restart the service after every settings change
    private void LoadSettings()
    {
        var hostSetting = _configurationService.ReadSetting(ConfigurationKey.ApiHost);
        _apiHost = string.IsNullOrEmpty(hostSetting) ? "localhost" : hostSetting;

        var portSetting = _configurationService.ReadSetting(ConfigurationKey.ApiPort);
        _apiPort = string.IsNullOrEmpty(portSetting) ? "11434" : portSetting;

        // Setting BaseAddress so all requests are directed towards this address by default
        var newBaseUri = new Uri($"http://{_apiHost}:{_apiPort}");
        if (_httpClient.BaseAddress == null || _httpClient.BaseAddress != newBaseUri)
        {
            _httpClient.Dispose();
            _httpClient = new HttpClient { BaseAddress = newBaseUri };
            _httpClient.Timeout = _timeout;
        }
    }

    public async Task Start()
    {
        if (_started)
        {
            return;
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            OllamaPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Programs\Ollama\ollama"
            );
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // same path on Mac and Linux
            OllamaPath = @"/usr/local/bin/ollama";
        }

        var ollamaProcessCount = GetProcessCountFunc();
        switch (ollamaProcessCount)
        {
            case 1:
                OnServiceStatusChanged(
                    ServiceStatus.Running,
                    LocalizationService.GetString("OLLAMA_ALREADY_RUNNING")
                );
                _started = true;
                return;
            case >= 2:
                // TODO: Automatically handling these processes instead of having to manually restart
                OnServiceStatusChanged(
                    ServiceStatus.Failed,
                    LocalizationService.GetString("MULTIPLE_INSTANCES_ERROR")
                );
                return;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = OllamaPath,
            Arguments = "serve",
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            _ollamaProcess = StartProcessFunc(startInfo);
        }
        catch (Win32Exception)
        {
            // This is the exception that is thrown when an Ollama process cant be started on the specified path
            // This is the first exception caught if Ollama isn't installed
            OnServiceStatusChanged(ServiceStatus.NotInstalled);
        }
        catch (Exception ex)
        {
            OnServiceStatusChanged(
                ServiceStatus.Failed,
                string.Format(LocalizationService.GetString("OLLAMA_FAILED"), ex.Message)
            );
            return;
        }

        // If an 'ollama' process can't be started then it's null, so it isn't installed
        // I don't think it could otherwise be null
        if (_ollamaProcess == null)
        {
            OnServiceStatusChanged(ServiceStatus.NotInstalled);
            return;
        }

        var isServerRunning = await IsOllamaServerRunning();
        if (isServerRunning)
        {
            OnServiceStatusChanged(
                ServiceStatus.Running,
                LocalizationService.GetString("OLLAMA_STARTED")
            );
            _started = true;
        }
        else
        {
            // Retries connection
            // This is necessary on macOS, because the server starts later than the IsOllamaServerRunning() call
            await Retry();
        }
    }

    // Method to guard api calls against being called before the Ollama process is started
    // Within methods that perform API calls this method should be called before any API call
    // Consumers must call Start() before calling an API method, or else an exception is thrown
    private void EnsureStarted()
    {
        if (!_started)
        {
            throw new InvalidOperationException("OllamaService has not been started yet. Call Start() first.");
        }
    }

    public async Task<bool> IsOllamaServerRunning()
    {
        LoadSettings();
        try
        {
            var response = await _httpClient.GetAsync("/api/version");
            return response.StatusCode == HttpStatusCode.OK;
        }
        catch
        {
            return false;
        }
    }

    public void Stop()
    {
        var ollamaProcessList = Process.GetProcessesByName("ollama");
        foreach (var process in ollamaProcessList)
        {
            KillProcess(process);
        }
        _httpClient.Dispose();
    }

    public async Task Retry()
    {
        // Sending Retrying event (for the HomeViewModel, so it shows the status on the UI)
        OnServiceStatusChanged(ServiceStatus.Retrying);

        var maxTotalWait = TimeSpan.FromSeconds(30); // Max full time while it retries
        var delay = TimeSpan.FromMilliseconds(200); // Starting delay
        var maxDelay = TimeSpan.FromSeconds(5); // Maximum delay
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < maxTotalWait)
        {
            if (await IsOllamaServerRunning())
            {
                OnServiceStatusChanged(
                    ServiceStatus.Running,
                    LocalizationService.GetString("OLLAMA_STARTED")
                );
                _started = true;
                return;
            }

            await Task.Delay(delay);

            // Increases the delay every time, but stays under the maximum
            delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, maxDelay.TotalMilliseconds));
        }

        OnServiceStatusChanged(
            ServiceStatus.Failed,
            LocalizationService.GetString("OLLAMA_CONNECTION_ERROR")
        );
    }

    private static void KillProcess(Process? process)
    {
        if (process == null || process.HasExited) return;
        process.Kill();
        process.Dispose();
    }

    public async Task<bool> IsModelDownloaded()
    {
        EnsureStarted();
        LoadSettings();

        try
        {
            var response = await _httpClient.GetAsync("/api/tags");
            var responseContent = await response.Content.ReadAsStringAsync();
            var json = JsonNode.Parse(responseContent);
            return json?["models"]?.AsArray().Any(m => m?["name"]?.ToString() == "llama3.2:latest" ||
                                                       m?["name"]?.ToString() == "llama3.2")
                   ?? false;
        }
        catch (JsonException ex)
        {
            _dialogService.ShowErrorDialog(ex.Message, false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            OnServiceStatusChanged(
                ServiceStatus.Failed,
                LocalizationService.GetString("OLLAMA_CONNECTION_ERROR")
            );
        }
        return false;
    }

    public async Task<string> GetModelParamNum(string modelName)
    {
        EnsureStarted();
        LoadSettings();

        var payload = new
        {
            model = modelName
        };

        var jsonPayload = JsonSerializer.Serialize(payload);
        var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.PostAsync("/api/show", content);
            var responseContent = await response.Content.ReadAsStringAsync();
            var json = JsonNode.Parse(responseContent);
            return (json?["details"]?["parameter_size"] == null ? "" : ":") +
                   json?["details"]?["parameter_size"]?.ToString().ToLower();
        }
        catch (JsonException ex)
        {
            _dialogService.ShowErrorDialog(ex.Message, false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            OnServiceStatusChanged(
                ServiceStatus.Failed,
                LocalizationService.GetString("OLLAMA_CONNECTION_ERROR")
            );
        }

        return string.Empty;
    }

    public async IAsyncEnumerable<OllamaResponse> GenerateMessage(List<Message> messageHistory, string modelName)
    {
        EnsureStarted();
        LoadSettings();

        var chatRequest = new ChatRequest(messageHistory, modelName);
        var jsonPayload = chatRequest.ToJson();

        var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = content
        };

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                // Upon a successful message send it also sends Ollama is running
                // If the host were to change then the HomeViewModel can display accordingly
                OnServiceStatusChanged(ServiceStatus.Running);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            OnServiceStatusChanged(
                ServiceStatus.Failed,
                LocalizationService.GetString("OLLAMA_CONNECTION_ERROR")
            );
            yield break;
        }

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            OllamaResponse? json = null;
            try
            {
                json = JsonSerializer.Deserialize<OllamaResponse>(line);
            }
            catch (JsonException e)
            {
                _dialogService.ShowErrorDialog(e.Message, false);
            }

            if (json != null)
            {
                yield return json;
            }
        }
    }

    public async IAsyncEnumerable<DownloadResponse> PullModel(string modelName)
    {
        EnsureStarted();
        LoadSettings();

        var payload = new
        {
            model = modelName,
            stream = true
        };

        var jsonPayload = JsonSerializer.Serialize(payload);

        var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/pull")
        {
            Content = content
        };

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                // Sends that Ollama is running upon an OK response
                // If the host were to change then the HomeViewModel can display accordingly
                OnServiceStatusChanged(ServiceStatus.Running);
            }

        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            OnServiceStatusChanged(
                ServiceStatus.Failed,
                LocalizationService.GetString("OLLAMA_CONNECTION_ERROR")
            );
            yield break;
        }

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        while (await reader.ReadLineAsync() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            DownloadResponse? json = null;
            try
            {
                json = JsonSerializer.Deserialize<DownloadResponse>(line);
            }
            catch (JsonException e)
            {
                _dialogService.ShowErrorDialog(e.Message, false);
            }

            if (json != null)
            {
                yield return json;
            }
        }
    }

    public async Task<List<OllamaModel>> ListLibraryModelsAsOllamaModelsAsync()
    {
        EnsureStarted();
        var libraryInfos = await new LibraryScraper(_httpClient).ListModelsFromLibraryAsync();

        return libraryInfos.Select(li => new OllamaModel
            {
                Name = li.Name,
                Format = string.Empty,
                Details = null,
                Size = 0,
                DownloadStatus = ModelDownloadStatus.Ready,
                RunsSlow = false
            })
            .ToList();
    }


    public async Task<bool> DeleteModel(string modelName)
    {
        EnsureStarted();
        LoadSettings();

        var payload = new
        {
            model = modelName
        };

         var jsonPayload = JsonSerializer.Serialize(payload);

         var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

         var request = new HttpRequestMessage(HttpMethod.Delete, "/api/delete")
         {
             Content = content
         };

         var response =  await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

         return response.StatusCode == HttpStatusCode.OK;
    }

    public async Task<ObservableCollection<OllamaModel>> ListDownloaded()
    {
        EnsureStarted();
        LoadSettings();

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/tags");
        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        var json = await response.Content.ReadAsStringAsync();

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var result = JsonSerializer.Deserialize<OllamaTagsResponse>(json, options);

        if (result?.Models == null)
            return [];

        return new ObservableCollection<OllamaModel>(
            result.Models.Select(m =>
            {
                // parse parameter size ("3.2B" → 3.2, etc.)
                double? parameters = null;
                if (!string.IsNullOrWhiteSpace(m.Details?.Parameter_Size))
                {
                    var cleaned = m.Details.Parameter_Size.TrimEnd('B', 'M', 'K');
                    if (double.TryParse(cleaned, out var parsed))
                        parameters = parsed;
                }

                // quantization ("Q4_K_M" → 4)
                int? quant = null;
                if (!string.IsNullOrWhiteSpace(m.Details?.Quantization_Level))
                {
                    var digits = new string(m.Details.Quantization_Level.TakeWhile(char.IsDigit).ToArray());
                    if (int.TryParse(digits, out var q))
                        quant = q;
                }

                var detailsDict = new Dictionary<string, string>();
                if (m.Details != null)
                {
                    detailsDict["Format"] = m.Details.Format ?? "";
                    detailsDict["Family"] = m.Details.Family ?? "";
                    detailsDict["Quantization"] = m.Details.Quantization_Level ?? "";
                    detailsDict["Parameter Size"] = m.Details.Parameter_Size ?? "";
                }

                return new OllamaModel(
                    name: m.Name!,
                    quantization: quant ?? 0,
                    parameters: parameters ?? double.NaN,
                    format: m.Details?.Format ?? string.Empty,
                    details: detailsDict,
                    size: (long)m.Size!,
                    downloadStatus: ModelDownloadStatus.Downloaded, // safe default
                    runsSlow: false
                );
            })
        );
    }

    // We call the event, meaning the subscribed methods are also called
    // One method subscribed to this in the HomeViewModel
    private void OnServiceStatusChanged(ServiceStatus? status, string? message = null)
    {
        CurrentServiceStatus = status;
        CurrentServiceMessage = message;

        // I added this because during testing an 'NSWindow should only be instantiated on the main thread!' exception was thrown
        // This is a macOS specific error I guess but let the UI calls be on the UI thread
        if (_dispatcher.CheckAccess())
        {
            ServiceStatusChanged?.Invoke(status, message);
        }
        else
        {
            _dispatcher.Post(() => { ServiceStatusChanged?.Invoke(status, message); });
        }
    }
}
