// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using avallama.Constants.Application;
using avallama.Constants.Keys;
using avallama.Constants.States;
using avallama.Models;
using avallama.Models.Ollama;
using avallama.Services;
using avallama.Services.Ollama;
using avallama.Services.Persistence;
using avallama.Utilities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace avallama.ViewModels;

// TODO: extract conversation logic into ConversationViewModel as the current implementation is not optimal

/// <summary>
/// ViewModel for the Home page, managing chat conversations, model selection,
/// and interactions with the Ollama service.
/// </summary>
public partial class HomeViewModel : PageViewModel
{
    #region Constants & Fields

    private const string OllamaDownloadUrl = @"https://ollama.com/download/";

    // Dependencies
    private readonly IOllamaService _ollamaService;
    private readonly IDialogService _dialogService;
    private readonly IConfigurationService _configurationService;
    private readonly IConversationService _conversationService;
    private readonly IUpdateService _updateService;
    private readonly IModelCacheService _modelCacheService;
    private readonly IMessenger _messenger;

    // Internal State
    private readonly ConditionalWeakTable<Conversation, ConversationState> _conversationStates = new();
    private bool _isInitializedAsync;
    private IList<OllamaModel> _previousDownloadedModels = [];

    // Backing fields for manual properties
    private ObservableStack<Conversation>? _conversations;
    private ObservableCollection<string> _availableModels;

    private TaskCompletionSource<bool> _isOllamaReady = new();

    #endregion

    #region Properties

    /// <summary>
    /// Localized warning message for low VRAM situations.
    /// </summary>
    public string ResourceLimitWarning { get; } = string.Format(LocalizationService.GetString("LOW_VRAM_WARNING"));

    /// <summary>
    /// Localized warning message when no models are downloaded.
    /// </summary>
    public string NoModelsDownloadedWarning { get; } =
        string.Format(LocalizationService.GetString("NOT_DOWNLOADED_WARNING"));

    /// <summary>
    /// Configuration setting for scrolling behavior.
    /// </summary>
    public string ScrollSetting = string.Empty;

    /// <summary>
    /// Stack of chat conversations.
    /// </summary>
    public ObservableStack<Conversation>? Conversations
    {
        get => _conversations;
        set => SetProperty(ref _conversations, value);
    }

    /// <summary>
    /// Gets or sets the name of the currently selected Ollama model.
    /// </summary>
    public string SelectedModelName
    {
        get;
        set
        {
            field = value;
            OnPropertyChanged();

            IsMessageBoxEnabled = !string.IsNullOrEmpty(value);
        }
    } = string.Empty;

    /// <summary>
    /// Gets or sets the text of the SearchBox.
    /// </summary>
    public string SearchBoxText
    {
        get;
        set
        {
            field = value;
            OnPropertyChanged();
            FilterConversations();
        }
    } = string.Empty;

    /// <summary>
    /// The collection of available Ollama models.
    /// </summary>
    public ObservableCollection<string> AvailableModels
    {
        get => _availableModels;
        set => SetProperty(ref _availableModels, value);
    }

    [ObservableProperty] private string _newMessageText = string.Empty;
    [ObservableProperty] private Conversation? _selectedConversation;

    // UI Visibility & State Flags
    [ObservableProperty] private bool _isResourceWarningVisible;
    [ObservableProperty] private bool _isNoModelsWarningVisible;
    [ObservableProperty] private bool _isMessageBoxEnabled;
    [ObservableProperty] private string _remoteConnectionText = string.Empty;
    [ObservableProperty] private bool _isRemoteConnectionTextVisible;
    [ObservableProperty] private bool _isRetryPanelVisible;
    [ObservableProperty] private bool _isRetryButtonVisible;
    [ObservableProperty] private string _retryInfoText = string.Empty;
    [ObservableProperty] private bool _showInformationalMessages;
    [ObservableProperty] private bool _isModelsDropdownEnabled;

    private IList<Conversation> _conversationsData = [];

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="HomeViewModel"/> class.
    /// </summary>
    /// <param name="ollamaService">Service for Ollama interactions.</param>
    /// <param name="dialogService">Service for displaying dialogs.</param>
    /// <param name="configurationService">Service for application settings.</param>
    /// <param name="conversationService">Service for conversation interactions.</param>
    /// <param name="updateService">Service for checking application updates.</param>
    /// <param name="modelCacheService">Service for caching models.</param>
    /// <param name="messenger">Messenger for cross-component communication.</param>
    public HomeViewModel(
        IOllamaService ollamaService,
        IDialogService dialogService,
        IConfigurationService configurationService,
        IConversationService conversationService,
        IUpdateService updateService,
        IModelCacheService modelCacheService,
        IMessenger messenger
    )
    {
        Page = ApplicationPage.Home;

        _ollamaService = ollamaService;
        _dialogService = dialogService;
        _configurationService = configurationService;
        _conversationService = conversationService;
        _updateService = updateService;
        _modelCacheService = modelCacheService;
        _messenger = messenger;
        _availableModels = [];

        _messenger.Register<ApplicationMessage.ReloadSettings>(this, (_, _) => { LoadSettings(); });

        LoadSettings();

        _ollamaService.StatusChanged += OllamaServiceStatusChanged;
    }

    #endregion

    #region Commands

    /// <summary>
    /// Performs asynchronous initialization of conversations and models.
    /// Typically called when the view is attached to the visual tree.
    /// </summary>
    [RelayCommand]
    public async Task InitializeAsync()
    {
        try
        {
            if (!_isInitializedAsync)
            {
                await InitializeConversations();

                if (_configurationService.ReadSetting(ConfigurationKey.IsUpdateCheckEnabled) == "True")
                    await CheckForUpdatesAsync();

                // get all downloaded models from cache database (with only their names, lightweight operation)
                if (_previousDownloadedModels.Count == 0)
                    _previousDownloadedModels = await _modelCacheService.GetDownloadedModelsAsync();
            }

            await InitializeModels();
            if (!_isInitializedAsync) _isInitializedAsync = true;
        }
        catch (Exception)
        {
            // TODO: proper logging
        }
    }

    /// <summary>
    /// Sends the current message text to the conversation, saves it to the database,
    /// and triggers the model response generation.
    /// </summary>
    [RelayCommand]
    private async Task SendMessage()
    {
        if (NewMessageText.Length == 0 || SelectedConversation == null || Conversations == null) return;

        NewMessageText = NewMessageText.Trim();
        var message = new Message(NewMessageText);

        SelectedConversation.Messages.Add(message);
        var newMessageId =
            await _conversationService.InsertMessage(SelectedConversation.Id, message, null, null);
        message.Id = newMessageId;

        NewMessageText = string.Empty;

        // Move conversation to top
        if (Conversations.IndexOf(SelectedConversation) != 0)
        {
            Conversations.Remove(SelectedConversation);
            Conversations.Push(SelectedConversation);
        }

        await AddGeneratedMessage(SelectedConversation);
    }

    /// <summary>
    /// Creates a new empty conversation and selects it.
    /// </summary>
    [RelayCommand]
    public async Task CreateNewConversation()
    {
        if (!string.IsNullOrEmpty(SearchBoxText))
        {
            SearchBoxText = string.Empty;
        }

        Conversations ??= [];
        var newConversation = new Conversation(
            LocalizationService.GetString("NEW_CONVERSATION"),
            string.Empty
        );
        newConversation.Id = await _conversationService.CreateConversation(newConversation);
        Conversations.Push(newConversation);
        _conversationsData.Insert(0, newConversation);
        SelectedConversation = newConversation;
    }

    /// <summary>
    /// Selects a conversation by its ID and loads its messages if not already loaded.
    /// </summary>
    /// <param name="parameter">The GUID of the conversation to select.</param>
    [RelayCommand]
    public async Task SelectConversation(object parameter)
    {
        if (parameter is not Guid guid || Conversations == null) return;

        if (guid == SelectedConversation?.Id) return;

        var selectedConversation = Conversations.FirstOrDefault(x => x.Id == guid);
        if (selectedConversation == null) return;

        SelectedConversation = selectedConversation;

        var state = GetState(SelectedConversation);
        if (state.MessagesLoaded) return;

        var messages = await _conversationService.GetMessagesForConversation(SelectedConversation);
        SelectedConversation.Messages = new ObservableCollection<Message>(messages);
        state.MessagesLoaded = true;
    }

    /// <summary>
    /// Deletes the specified conversation after user confirmation.
    /// </summary>
    /// <param name="parameter">The GUID of the conversation to delete.</param>
    [RelayCommand]
    public async Task DeleteConversation(object parameter)
    {
        if (parameter is not Guid guid || SelectedConversation == null || Conversations == null) return;

        var res = await _dialogService.ShowConfirmationDialogAsync(
            LocalizationService.GetString("CONFIRM_DELETION_DIALOG_TITLE"),
            LocalizationService.GetString("DELETE"),
            LocalizationService.GetString("CANCEL"),
            string.Format(LocalizationService.GetString("CONFIRM_DELETION_DIALOG_DESC"),
                LocalizationService.GetString("THIS_CONVERSATION")), ConfirmationType.Positive);

        if (res is ConfirmationResult { Confirmation: ConfirmationType.Negative }) return;

        await _conversationService.DeleteConversation(guid);
        Conversations.Remove(SelectedConversation);
        _conversationsData.Remove(SelectedConversation);

        if (_conversationsData.Count == 0)
        {
            await CreateNewConversation();
            return;
        }

        var newIndex = Math.Min(Conversations.IndexOf(SelectedConversation) + 1, Conversations.Count - 1);
        SelectedConversation = Conversations[newIndex];
    }

    /// <summary>
    /// Deletes the specified message.
    /// </summary>
    /// <param name="parameter">Message to delete</param>
    [RelayCommand]
    public async Task DeleteMessage(object parameter)
    {
        if (parameter is not Message messageToDelete) return;

        // id: -1 is a failed message
        if (messageToDelete.Id >= 0 && messageToDelete is not FailedMessage or TypingIndicatorMessage)
        {
            await _conversationService.DeleteMessage(messageToDelete.Id);
        }

        SelectedConversation?.Messages.Remove(messageToDelete);
    }

    /// <summary>
    /// Manually triggers retry for the Ollama connection.
    /// </summary>
    [RelayCommand]
    public async Task RetryOllamaConnection()
    {
        await _ollamaService.RetryConnectionAsync();
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Orchestrates the generation of the model response, handling streaming chunks,
    /// calculation of generation speed, and potential title regeneration.
    /// </summary>
    private async Task AddGeneratedMessage(Conversation conversation)
    {
        var generatedMessage = new GeneratedMessage("", 0.0);
        conversation.Messages.Add(generatedMessage);

        var messageHistory = new List<Message>(conversation.Messages.ToList());
        messageHistory.RemoveAt(messageHistory.Count - 1);

        // Remove failed/typing messages from history before generation
        messageHistory.RemoveAll(message => message is FailedMessage or TypingIndicatorMessage);

        await foreach (var chunk in _ollamaService.GenerateMessageAsync(messageHistory, SelectedModelName))
        {
            if (chunk.Message != null) generatedMessage.Content += chunk.Message.Content;

            if (chunk is { EvalCount: not null, EvalDuration: not null })
            {
                double tokensPerSecond =
                    chunk.EvalCount.GetValueOrDefault() / (double)chunk.EvalDuration * Math.Pow(10, 9);
                generatedMessage.GenerationSpeed = tokensPerSecond;
                IsResourceWarningVisible = tokensPerSecond < 20;

                // Regenerate title after the first 2 messages and then every 6 messages (1 & 3 exchanges)
                if ((conversation.Messages.Count - 2) % 6 == 0 || conversation.Messages.Count == 2)
                {
                    await RegenerateConversationTitle(conversation);
                }

                // Also regenerate if the title is still "New Conversation", but it was not updated after the first message exchange,
                // this can happen if the application is closed while a new conversation is being generated.
                // Let's just hope they didn't also switch their localization settings in the meantime :)
                if (conversation.Title == LocalizationService.GetString("NEW_CONVERSATION") &&
                    conversation.Messages.Count == 4)
                {
                    await RegenerateConversationTitle(conversation);
                }
            }
        }

        var generatedMessageId = await _conversationService.InsertMessage(conversation.Id, generatedMessage,
            SelectedModelName, generatedMessage.GenerationSpeed);
        generatedMessage.Id = generatedMessageId;
        conversation.Model = SelectedModelName;
    }

    /// <summary>
    /// Generates a short title for the conversation based on the message history.
    /// </summary>
    private async Task RegenerateConversationTitle(Conversation conversation)
    {
        conversation.Title = string.Empty;

        // TODO: better solution for title generation (not working for all models)
        const string request =
            "Generate only a single short title for this conversation with no use of quotation marks.";

        var tmpMessage = new Message(request);
        var messageHistory = new List<Message>(conversation.Messages.ToList()) { tmpMessage };
        await foreach (var chunk in _ollamaService.GenerateMessageAsync(messageHistory, SelectedModelName))
        {
            if (chunk.Message != null) conversation.Title += chunk.Message.Content;
        }

        await _conversationService.UpdateConversationTitle(conversation);
    }

    /// <summary>
    /// Initializes the conversation list from the database (it's called only once).
    /// </summary>
    private async Task InitializeConversations()
    {
        _conversationsData = await _conversationService.GetConversations();
        Conversations = new ObservableStack<Conversation>(_conversationsData);
        if (_conversations is not { Count: > 0 })
        {
            await CreateNewConversation();
            return;
        }

        SelectedConversation = _conversations.FirstOrDefault()!;
        var messages = await _conversationService.GetMessagesForConversation(SelectedConversation);
        SelectedConversation.Messages = new ObservableCollection<Message>(messages);
        GetState(SelectedConversation).MessagesLoaded = true;
    }

    /// <summary>
    /// Initializes available models by querying the Ollama service (called when the View is initialized).
    /// </summary>
    private async Task InitializeModels()
    {
        // cancel the previous connection waiting Task and initialize a new one if it's completed/API was connected
        if (_isOllamaReady.Task.IsCompleted ||
            _ollamaService.CurrentServiceStatus.ServiceState == OllamaServiceState.Ready)
        {
            _isOllamaReady.TrySetCanceled();
            _isOllamaReady = new TaskCompletionSource<bool>();
        }
        else
        {
            // waits for the Ollama API connection
            await _isOllamaReady.Task;
        }

        // caches the previously selected model name
        var tmpName = string.Empty;
        if (_isInitializedAsync) tmpName = SelectedModelName;

        // get all downloaded models from Ollama API (with only their names)
        var downloadedModels = await _ollamaService.GetDownloadedModelsAsync();

        // we compare model names that were set in the previous initialization and model names coming from the API
        var currentNames = downloadedModels.Select(m => m.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var previousNames = _previousDownloadedModels.Select(m => m.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var hasChanges = !currentNames.SetEquals(previousNames);

        // if the names differ we set deleted models to deleted in cache db and upsert downloaded models with all info
        if (hasChanges)
        {
            // deleted models
            var deletedModels = _previousDownloadedModels.Where(m => !currentNames.Contains(m.Name)).ToList();

            foreach (var deletedModel in deletedModels)
            {
                deletedModel.IsDownloaded = false;
                await _modelCacheService.UpdateModelAsync(deletedModel);
            }

            // newly downloaded models (either downloaded from the app or outside with Ollama CLI)
            var newModels = downloadedModels.Where(m => !previousNames.Contains(m.Name)).ToList();

            foreach (var newModel in newModels)
            {
                newModel.IsDownloaded = true;

                // enriches the model with info coming from '/api/tags' and '/api/show'
                await _ollamaService.EnrichModelAsync(newModel);

                // maybe change this later to an upsert operation, but not important atm
                if (await _modelCacheService.ContainsModelAsync(newModel))
                {
                    await _modelCacheService.UpdateModelAsync(newModel);
                }
                else
                {
                    await _modelCacheService.InsertModelAsync(newModel);
                }
            }
        }

        _previousDownloadedModels = downloadedModels;

        // refresh UI and set status correctly based on downloaded models
        AvailableModels.Clear();
        if (downloadedModels.Count == 0)
        {
            IsNoModelsWarningVisible = true;
            IsMessageBoxEnabled = false;
            SelectedModelName = string.Empty;
            IsModelsDropdownEnabled = false;
        }
        else
        {
            // add downloaded models to the ComboBox
            foreach (var model in downloadedModels)
            {
                AvailableModels.Add(model.Name);
            }

            IsModelsDropdownEnabled = true;
            IsNoModelsWarningVisible = false;
            IsMessageBoxEnabled = true;

            if (!_isInitializedAsync)
            {
                // selects the first model
                SelectedModelName = AvailableModels[0];
            }
            else if (_isInitializedAsync && !string.IsNullOrEmpty(tmpName))
            {
                // sets the previously selected model
                SelectedModelName = AvailableModels.Contains(tmpName)
                    ? tmpName
                    : AvailableModels.FirstOrDefault() ?? string.Empty;
            }
        }
    }

    /// <summary>
    /// Calls <see cref="UpdateService"/> to check if a new version of the application is available.
    /// If an update is available, shows a dialog prompting the user to visit the GitHub releases page.
    /// If the <see cref="ConfigurationKey"/> IsUpdateCheckEnabled is false, this method is not called.
    /// </summary>
    private async Task CheckForUpdatesAsync()
    {
        if (await _updateService.IsUpdateAvailableAsync())
        {
            _dialogService.ShowActionDialog(
                LocalizationService.GetString("UPDATE_AVAILABLE"),
                LocalizationService.GetString("OPEN_GITHUB"),
                () =>
                {
                    Process.Start(new ProcessStartInfo
                        {
                            FileName = "https://github.com/4foureyes/avallama/releases/latest",
                            UseShellExecute = true
                        }
                    );
                },
                null,
                LocalizationService.GetString("UPDATE_AVAILABLE_DESC"),
                false
            );
        }
    }

    /// <summary>
    /// Filters the visible conversations list based on the search query using fuzzy matching.
    /// Reverts to the full conversation list if the search box is empty.
    /// </summary>
    private void FilterConversations()
    {
        if (_conversationsData.Count == 0) return;

        var search = SearchBoxText.Trim();

        if (string.IsNullOrEmpty(search))
        {
            Conversations = new ObservableStack<Conversation>(_conversationsData);
            return;
        }

        var filteredList = _conversationsData
            .Select(c => new
            {
                Conversation = c,
                Score = SearchUtilities.CalculateMatchScore(c.Title, search)
            })
            .Where(x => x.Score >= 25)
            .OrderByDescending(x => x.Score)
            .Select(x => x.Conversation);

        Conversations = new ObservableStack<Conversation>(filteredList);
    }

    /// <summary>
    /// Handles changes in the unified Ollama status and updates UI elements accordingly.
    /// </summary>
    private void OllamaServiceStatusChanged(OllamaServiceStatus status)
    {
        switch (status.ServiceState)
        {
            case OllamaServiceState.Starting:
                SetViewState(true, false, false, false,
                    LocalizationService.GetString("CONNECTING"));
                break;

            case OllamaServiceState.Ready:
                _isOllamaReady.TrySetResult(true);
                var apiHost = _configurationService.ReadSetting(ConfigurationKey.ApiHost);
                var apiPort = _configurationService.ReadSetting(ConfigurationKey.ApiPort);

                if (OllamaApiClient.IsConnectionRemote(apiHost))
                {
                    RemoteConnectionText = string.Format(LocalizationService.GetString("REMOTE_CONNECTION"),
                        apiHost + ":" + apiPort);
                    IsRemoteConnectionTextVisible = true;
                }
                else
                {
                    IsRemoteConnectionTextVisible = false;
                }

                SetViewState(false, false, !string.IsNullOrEmpty(SelectedModelName), true);
                break;

            case OllamaServiceState.Failed:
            case OllamaServiceState.Stopped:
                ReplaceGeneratedMessageToFailed();
                IsRemoteConnectionTextVisible = false;
                SetViewState(true, true, false, false,
                    status.Message ?? LocalizationService.GetString("OLLAMA_CONNECTION_ERROR"));
                break;

            case OllamaServiceState.NotInstalled:
                // TODO:
                // change this later to not shutdown
                // so the user still can use the app (browse conversations, fetch model metadata from Ollama website etc.)
                _dialogService.ShowActionDialog(
                    title: LocalizationService.GetString("OLLAMA_NOT_INSTALLED"),
                    actionButtonText: LocalizationService.GetString("DOWNLOAD"),
                    action: () =>
                    {
                        RedirectToOllamaDownload();
                        _messenger.Send(new ApplicationMessage.Shutdown());
                    },
                    closeAction: () => { _messenger.Send(new ApplicationMessage.Shutdown()); },
                    description: LocalizationService.GetString("OLLAMA_NOT_INSTALLED_DESC"),
                    actionButtonOnly: false
                );
                break;
        }
    }

    /// <summary>
    /// Updates the visibility and state of the view's elements based on the provided parameters.
    /// This method provides a centralized way to manage the UI state during connection and process lifecycle changes.
    /// </summary>
    /// <param name="isRetryPanelVisible">Determines whether the retry panel is visible to the user.</param>
    /// <param name="isRetryButtonVisible">Determines whether the retry button within the panel is displayed.</param>
    /// <param name="isMessageBoxEnabled">Controls whether the message text box is enabled.</param>
    /// <param name="isModelsDropdownEnabled">Controls whether the model selection dropdown menu is enabled.</param>
    /// <param name="retryInfoText">The status or error message to display. If null, an empty string is used.</param>
    private void SetViewState(
        bool isRetryPanelVisible,
        bool isRetryButtonVisible,
        bool isMessageBoxEnabled,
        bool isModelsDropdownEnabled,
        string? retryInfoText = null
    )
    {
        IsRetryPanelVisible = isRetryPanelVisible;
        IsRetryButtonVisible = isRetryButtonVisible;
        IsMessageBoxEnabled = isMessageBoxEnabled;
        IsModelsDropdownEnabled = isModelsDropdownEnabled;
        RetryInfoText = retryInfoText ?? string.Empty;
    }

    /// <summary>
    /// Replaces the last generated message to a Failed message (used when an error occurs during generation).
    /// </summary>
    private void ReplaceGeneratedMessageToFailed()
    {
        // If the last message meant to be a generated one, replace it with a FailedMessage
        if (SelectedConversation is not { Messages.Count: > 0 }) return;
        if (SelectedConversation.Messages.Last() is not GeneratedMessage generatedMessage) return;
        SelectedConversation.Messages.Remove(generatedMessage);
        SelectedConversation.Messages.Add(new FailedMessage(LocalizationService.GetString("MESSAGE_GENERATION_FAILED")));
    }

    private void LoadSettings()
    {
        ScrollSetting = _configurationService.ReadSetting(ConfigurationKey.ScrollToBottom);
        ShowInformationalMessages =
            _configurationService.ReadSetting(ConfigurationKey.IsInformationalMessagesVisible) == "True";
    }

    private ConversationState GetState(Conversation conversation)
    {
        return _conversationStates.GetOrCreateValue(conversation);
    }

    /// <summary>
    /// Redirects to Ollama's download website based on the running operating system.
    /// </summary>
    private static void RedirectToOllamaDownload()
    {
        var processUrl = OllamaDownloadUrl;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            processUrl += "windows";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            processUrl += "linux";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            processUrl += "mac";
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = processUrl,
            UseShellExecute = true
        });
    }

    #endregion

    #region Helper Types

    /// <summary>
    /// Helper class to maintain transient state for conversation objects (e.g., whether messages are loaded),
    /// avoiding database queries for already loaded data.
    /// </summary>
    internal class ConversationState
    {
        public bool MessagesLoaded { get; set; }
    }

    #endregion
}
