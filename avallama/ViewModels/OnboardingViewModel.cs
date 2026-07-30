// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using avallama.Constants;
using avallama.Constants.Application;
using avallama.Constants.Keys;
using avallama.Constants.States;
using avallama.Models.Ollama;
using avallama.Services.Ollama;
using avallama.Services.Persistence;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace avallama.ViewModels;

/// <summary>
/// ViewModel for the onboarding process, handling initial setup steps like Ollama connection testing and scraper configuration.
/// </summary>
public partial class OnboardingViewModel : PageViewModel
{
    #region Dependencies & Fields

    private readonly IOllamaService _ollamaService;
    private readonly IConfigurationService _configurationService;

    private CancellationTokenSource? _connectionTestCts;
    private string _previousHost = string.Empty;
    private string _previousPort = string.Empty;

    // implemented as a stack to support future scalability
    // while currently only handling two views, this allows easy addition of new onboarding steps (like theme customization)
    private readonly Stack<OnboardingContent> _navigationStack = new();

    #endregion

    #region Observable Properties

    /// <summary>
    /// Gets or sets the current content/step displayed in the onboarding flow.
    /// </summary>
    [ObservableProperty] private OnboardingContent _content = OnboardingContent.Connection;

    /// <summary>
    /// Gets or sets the API host address input by the user.
    /// </summary>
    [ObservableProperty] private string _apiHostText = "localhost";

    /// <summary>
    /// Gets or sets the API port input by the user.
    /// </summary>
    [ObservableProperty] private string _apiPortText = "11434";

    /// <summary>
    /// Gets the current status of the Ollama service.
    /// </summary>
    public OllamaServiceStatus OllamaStatus => _ollamaService.CurrentServiceStatus;

    /// <summary>
    /// Gets a value indicating whether the application is successfully connected to the Ollama service.
    /// </summary>
    public bool IsOllamaConnected => _ollamaService.CurrentServiceStatus.ServiceState == OllamaServiceState.Ready;

    /// <summary>
    /// Gets a value indicating whether the user can navigate back to a previous onboarding step.
    /// </summary>
    public bool IsBackEnabled => _navigationStack.Count > 0;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="OnboardingViewModel"/> class.
    /// </summary>
    /// <param name="ollamaService">The service for Ollama interactions.</param>
    /// <param name="configurationService">The service for persisting application settings.</param>
    public OnboardingViewModel(
        IOllamaService ollamaService,
        IConfigurationService configurationService)
    {
        Page = ApplicationPage.Onboarding;

        _ollamaService = ollamaService;
        _configurationService = configurationService;

        var apiHost = _configurationService.ReadSetting(ConfigurationKey.ApiHost);
        var apiPort = _configurationService.ReadSetting(ConfigurationKey.ApiPort);

        if (!string.IsNullOrEmpty(apiHost)) ApiHostText = apiHost;
        if (!string.IsNullOrEmpty(apiPort)) ApiPortText = apiPort;

        _ollamaService.StatusChanged += OllamaServiceStatusChanged;
    }

    #endregion

    #region Commands

    /// <summary>
    /// Navigates to a specific onboarding step and pushes the current step to the navigation stack.
    /// </summary>
    /// <param name="parameter">The target <see cref="OnboardingContent"/> to navigate to.</param>
    [RelayCommand]
    public void NavigateTo(object parameter)
    {
        if (parameter is not OnboardingContent content) return;

        _navigationStack.Push(Content);
        Content = content;

        OnPropertyChanged(nameof(IsBackEnabled));
    }

    /// <summary>
    /// Navigates back to the previous onboarding step using the navigation stack.
    /// </summary>
    [RelayCommand]
    public void NavigateBack()
    {
        if (_navigationStack.Count == 0) return;

        var content = _navigationStack.Pop();
        Content = content;

        OnPropertyChanged(nameof(IsBackEnabled));
    }

    /// <summary>
    /// Skips the ongoing connection test by cancelling the request and navigates to the scraper setup step.
    /// </summary>
    [RelayCommand]
    public void SkipConnectionTest()
    {
        // cancels the ongoing network request to free up resources
        _connectionTestCts?.Cancel();
        NavigateTo(OnboardingContent.Scraper);
    }

    /// <summary>
    /// Asynchronously tests the connection to the Ollama API using the provided host and port.
    /// </summary>
    [RelayCommand]
    public async Task TestConnectionAsync()
    {
        // prevent redundant checks if already connected with the same credentials
        if (_ollamaService.CurrentServiceStatus.ServiceState == OllamaServiceState.Ready
            && _previousHost == ApiHostText && _previousPort == ApiPortText) return;

        // cancel any previously running connection test before starting a new one
        _connectionTestCts?.Cancel();
        _connectionTestCts?.Dispose();
        _connectionTestCts = new CancellationTokenSource();
        var token = _connectionTestCts.Token;

        _configurationService.SaveSetting(ConfigurationKey.ApiHost, ApiHostText);
        _configurationService.SaveSetting(ConfigurationKey.ApiPort, ApiPortText);

        try
        {
            await _ollamaService.CheckConnectionAsync(token);

            _previousHost = ApiHostText;
            _previousPort = ApiPortText;
        }
        catch (OperationCanceledException)
        {
            // ignore the exception, as we only catch it to avoid crashing when the user clicks skip
        }
        finally
        {
            _connectionTestCts?.Dispose();
            _connectionTestCts = null;
        }
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// Handles changes in the Ollama service status and updates the UI bindings accordingly.
    /// </summary>
    /// <param name="status">The updated status of the service.</param>
    private void OllamaServiceStatusChanged(OllamaServiceStatus status)
    {
        OnPropertyChanged(nameof(OllamaStatus));
        OnPropertyChanged(nameof(IsOllamaConnected));
    }

    #endregion
}
