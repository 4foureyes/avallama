// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using avallama.Constants.Application;
using avallama.Constants.Keys;
using avallama.Constants.States;
using avallama.Models;
using avallama.Models.Ollama;
using avallama.Services;
using avallama.Services.Ollama;
using avallama.Services.Persistence;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace avallama.ViewModels;

/// <summary>
/// ViewModel responsible for managing a single conversation's state and logic.
/// </summary>
public partial class ConversationViewModel : ViewModelBase, IDisposable
{
    #region Dependencies & Fields

    private readonly IOllamaService _ollamaService;
    private readonly IDialogService _dialogService;
    private readonly IConfigurationService _configurationService;
    private readonly IConversationService _conversationService;
    private readonly IMessenger _messenger;

    private bool _isInitializedAsync;
    private CancellationTokenSource _generationCts = new();

    /// <summary>
    /// Gets or sets the scroll behavior setting for the conversation view.
    /// </summary>
    public string ScrollSetting = string.Empty;

    #endregion

    #region Observable Properties

    [ObservableProperty] private Conversation _conversation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInitializing))]
    [NotifyPropertyChangedFor(nameof(IsGenerating))]
    private ConversationStatus _status = new(ConversationState.Initializing);

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsMessageBoxEnabled))]
    private string _selectedModelName = string.Empty;

    [ObservableProperty] private bool _showInformationalMessages;
    [ObservableProperty] private bool _isRemoteTextVisible;
    [ObservableProperty] private bool _isRunningSlowTextVisible;
    [ObservableProperty] private bool _isNoModelsTextVisible;
    [ObservableProperty] private string _remoteText = string.Empty;
    [ObservableProperty] private string _newMessageText = string.Empty;

    #endregion

    #region Computed Properties

    /// <summary>
    /// Gets the warning text when no models are downloaded.
    /// </summary>
    public string NoModelsText { get; } = LocalizationService.GetString("NOT_DOWNLOADED_WARNING");

    /// <summary>
    /// Gets or sets the collection of available models.
    /// </summary>
    public ObservableCollection<string> AvailableModels { get; set; }

    /// <summary>
    /// Indicates whether the conversation is currently initializing.
    /// </summary>
    public bool IsInitializing => Status.ConversationState == ConversationState.Initializing;

    /// <summary>
    /// Indicates whether the model is currently generating a response.
    /// </summary>
    public bool IsGenerating =>
        Status.ConversationState is ConversationState.ProcessingMessage or ConversationState.StreamingResponse;

    /// <summary>
    /// Indicates whether the message input box should be enabled.
    /// </summary>
    public bool IsMessageBoxEnabled => !string.IsNullOrWhiteSpace(SelectedModelName);

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="ConversationViewModel"/> class.
    /// </summary>
    public ConversationViewModel(
        Conversation conversation,
        IOllamaService ollamaService,
        IDialogService dialogService,
        IConfigurationService configurationService,
        IConversationService conversationService,
        IMessenger messenger,
        ObservableCollection<string> availableModels)
    {
        _conversation = conversation;
        _ollamaService = ollamaService;
        _dialogService = dialogService;
        _configurationService = configurationService;
        _conversationService = conversationService;
        _messenger = messenger;

        AvailableModels = availableModels;

        if (AvailableModels.Count > 0)
        {
            SelectedModelName = AvailableModels[0];
        }

        _messenger.Register<ApplicationMessage.ReloadSettings>(this, (_, _) => { LoadSettings(); });

        LoadSettings();

        _ollamaService.StatusChanged += OllamaServiceStatusChanged;
        AvailableModels.CollectionChanged += AvailableModels_CollectionChanged;
    }

    #endregion

    #region Public Methods & Commands

    /// <summary>
    /// Initializes the conversation view asynchronously by loading history from the database.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_isInitializedAsync) return;

        Status = new ConversationStatus(ConversationState.Initializing);
        var messages = await _conversationService.GetMessagesForConversation(Conversation);
        Conversation.Messages = new ObservableCollection<Message>(messages);
        Status = new ConversationStatus(ConversationState.Idle);

        _isInitializedAsync = true;
    }

    /// <summary>
    /// Cancels the ongoing message generation process.
    /// </summary>
    [RelayCommand]
    public void CancelGeneration()
    {
        _generationCts.Cancel();
    }

    /// <summary>
    /// Deletes a specific message from the conversation and database.
    /// </summary>
    /// <param name="parameter">The message object to delete.</param>
    [RelayCommand]
    public async Task DeleteMessageAsync(object parameter)
    {
        if (parameter is not Message msg) return;

        // cancel generation if the currently generating message is deleted
        if (IsGenerating && (Conversation.Messages.LastOrDefault() == msg || msg is TypingIndicatorMessage))
        {
            CancelGeneration();
        }

        if (msg is not FailedMessage or TypingIndicatorMessage)
        {
            await _conversationService.DeleteMessage(msg.Id);
        }

        Conversation.Messages.Remove(msg);
    }

    /// <summary>
    /// Retries generating a response after a failure, restarting the Ollama process if necessary.
    /// </summary>
    /// <param name="parameter">The failed message to remove.</param>
    [RelayCommand]
    private async Task RetryMessageAsync(object parameter)
    {
        if (IsGenerating || parameter is not FailedMessage failedMessage) return;

        Conversation.Messages.Remove(failedMessage);

        Status = new ConversationStatus(ConversationState.ProcessingMessage);

        var typingIndicator = new TypingIndicatorMessage();
        Conversation.Messages.Add(typingIndicator);

        if (_ollamaService.CurrentServiceStatus.ServiceState != OllamaServiceState.Ready)
        {
            await _ollamaService.RetryConnectionAsync();

            if (_ollamaService.CurrentServiceStatus.ServiceState != OllamaServiceState.Ready)
            {
                return;
            }
        }

        _generationCts.Cancel();
        _generationCts = new CancellationTokenSource();

        await GenerateMessageAsync();
    }

    /// <summary>
    /// Sends a new message and initiates the generation process.
    /// </summary>
    [RelayCommand]
    private async Task SendMessageAsync()
    {
        if (IsGenerating) return;

        _generationCts.Cancel();
        _generationCts = new CancellationTokenSource();

        if (NewMessageText.Length == 0 || string.IsNullOrWhiteSpace(NewMessageText)) return;

        NewMessageText = NewMessageText.Trim();

        var msg = new Message(NewMessageText);
        var newMessageId = await _conversationService.InsertMessage(Conversation.Id, msg, null, null);
        msg.Id = newMessageId;
        Conversation.Messages.Add(msg);

        Status = new ConversationStatus(ConversationState.ProcessingMessage);

        var typingIndicator = new TypingIndicatorMessage();
        Conversation.Messages.Add(typingIndicator);

        NewMessageText = string.Empty;

        _messenger.Send(new ApplicationMessage.ConversationBump(this));

        await GenerateMessageAsync();
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Generates the response stream from the Ollama service.
    /// </summary>
    private async Task GenerateMessageAsync()
    {
        var generatedMessage = new GeneratedMessage();
        var messageHistory = GetMessageHistory();
        var token = _generationCts.Token;

        try
        {
            await foreach (var chunk in _ollamaService.GenerateMessageAsync(messageHistory, SelectedModelName, token))
            {
                if (chunk.Message != null)
                {
                    generatedMessage.Content += chunk.Message.Content;

                    if (Status.ConversationState == ConversationState.ProcessingMessage)
                    {
                        Status = new ConversationStatus(ConversationState.StreamingResponse);

                        Dispatcher.UIThread.Post(() =>
                        {
                            if (Conversation.Messages.LastOrDefault() is TypingIndicatorMessage)
                            {
                                Conversation.Messages.RemoveAt(Conversation.Messages.Count - 1);
                            }

                            Conversation.Messages.Add(generatedMessage);
                        });
                    }
                }

                if (chunk is { EvalCount: not null, EvalDuration: not null })
                {
                    var tokensPerSecond = chunk.EvalCount.GetValueOrDefault() / (double)chunk.EvalDuration *
                                          Math.Pow(10, 9);

                    generatedMessage.GenerationSpeed = tokensPerSecond;
                    IsRunningSlowTextVisible = tokensPerSecond < 20;

                    // regenerate title after the first 2 messages and then every 6 messages (1 & 3 exchanges)
                    if ((Conversation.Messages.Count - 2) % 6 == 0 || Conversation.Messages.Count == 2)
                    {
                        await GenerateTitle(token);
                    }

                    // also regenerate if the title is still "new conversation", but it was not updated after the first message exchange,
                    // this can happen if the application is closed while a new conversation is being generated.
                    // let's just hope they didn't also switch their localization settings in the meantime :)
                    if (Conversation.Title == LocalizationService.GetString("NEW_CONVERSATION") &&
                        Conversation.Messages.Count == 4)
                    {
                        await GenerateTitle(token);
                    }
                }
            }

            var generatedMessageId = await _conversationService.InsertMessage(Conversation.Id, generatedMessage,
                SelectedModelName, generatedMessage.GenerationSpeed);
            generatedMessage.Id = generatedMessageId;
            Conversation.Model = SelectedModelName;

            Status = new ConversationStatus(ConversationState.Idle);
        }
        catch (OperationCanceledException)
        {
            if (Status.ConversationState != ConversationState.Failed)
            {
                Status = new ConversationStatus(ConversationState.Idle);

                if (!string.IsNullOrWhiteSpace(generatedMessage.Content) && generatedMessage.Id == 0)
                {
                    // todo: calculate generation speed real time
                    // set this to -1 for now to display "generation canceled" message
                    generatedMessage.GenerationSpeed = -1;

                    var generatedMessageId = await _conversationService.InsertMessage(Conversation.Id, generatedMessage,
                        SelectedModelName, generatedMessage.GenerationSpeed);
                    generatedMessage.Id = generatedMessageId;
                    Conversation.Model = SelectedModelName;
                }
                else
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        var lastMessage = Conversation.Messages.LastOrDefault();
                        if (lastMessage is TypingIndicatorMessage)
                        {
                            Conversation.Messages.Remove(lastMessage);
                        }
                    });
                }
            }
        }
        catch (Exception ex)
        {
            HandleFailedGeneration(ex.Message);
        }
    }

    /// <summary>
    /// Generates a short title for the current conversation.
    /// </summary>
    private async Task GenerateTitle(CancellationToken token)
    {
        Conversation.Title = string.Empty;

        // TODO: better solution for title generation (not working for all models)
        // possible solution: use a really tiny model that is capable of proper title generation, only if the user allows
        const string request =
            "Summarize the conversation in a short title. Reply with ONLY the title itself. Do NOT include quotation marks, formatting, or any conversational filler.";

        var tmpMessage = new Message(request);
        var messageHistory = new List<Message>(GetMessageHistory()) { tmpMessage };

        await foreach (var chunk in _ollamaService.GenerateMessageAsync(messageHistory, SelectedModelName, token))
        {
            if (chunk.Message != null) Conversation.Title += chunk.Message.Content;
        }

        await _conversationService.UpdateConversationTitle(Conversation);
    }

    /// <summary>
    /// Retrieves the valid message history to be sent to the model.
    /// </summary>
    private List<Message> GetMessageHistory()
    {
        var messages = Conversation.Messages.ToList();
        messages.RemoveAll(msg => msg.Content == string.Empty || msg is FailedMessage or TypingIndicatorMessage);
        return messages;
    }

    /// <summary>
    /// Loads configuration settings from the configuration service.
    /// </summary>
    private void LoadSettings()
    {
        ScrollSetting = _configurationService.ReadSetting(ConfigurationKey.ScrollToBottom);
        ShowInformationalMessages =
            _configurationService.ReadSetting(ConfigurationKey.IsInformationalMessagesVisible) == "True";
    }

    /// <summary>
    /// Opens the default browser to the Ollama download page based on the current OS.
    /// </summary>
    private static void RedirectToOllamaDownload()
    {
        var processUrl = OllamaService.DownloadUrl;
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

    /// <summary>
    /// Handles the scenario where the generation fails, updating the UI accordingly.
    /// </summary>
    private void HandleFailedGeneration(string errorMessage)
    {
        Status = new ConversationStatus(ConversationState.Failed, errorMessage);

        Dispatcher.UIThread.Post(() =>
        {
            var lastMessage = Conversation.Messages.LastOrDefault();

            if (lastMessage is FailedMessage) return;

            if (lastMessage is TypingIndicatorMessage or GeneratedMessage)
            {
                Conversation.Messages.Remove(lastMessage);
            }

            Conversation.Messages.Add(new FailedMessage(errorMessage));
        });
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// Handles changes in the Ollama service status.
    /// </summary>
    private void OllamaServiceStatusChanged(OllamaServiceStatus status)
    {
        switch (status.ServiceState)
        {
            case OllamaServiceState.Starting:
                break;

            case OllamaServiceState.Ready:
                var apiHost = _configurationService.ReadSetting(ConfigurationKey.ApiHost);
                var apiPort = _configurationService.ReadSetting(ConfigurationKey.ApiPort);

                if (OllamaApiClient.IsConnectionRemote(apiHost))
                {
                    RemoteText = string.Format(LocalizationService.GetString("REMOTE_CONNECTION"),
                        apiHost + ":" + apiPort);
                    IsRemoteTextVisible = true;
                }
                else
                {
                    IsRemoteTextVisible = false;
                }

                break;

            case OllamaServiceState.Failed:
            case OllamaServiceState.Stopped:
                if (IsGenerating)
                {
                    _generationCts.Cancel();
                    HandleFailedGeneration(status.Message ?? LocalizationService.GetString("OLLAMA_CONNECTION_ERROR"));
                }
                else
                {
                    Status = new ConversationStatus(ConversationState.Failed);
                }

                break;

            case OllamaServiceState.NotInstalled:
                _dialogService.ShowActionDialog(
                    title: LocalizationService.GetString("OLLAMA_NOT_INSTALLED"),
                    actionButtonText: LocalizationService.GetString("DOWNLOAD"),
                    action: RedirectToOllamaDownload,
                    closeAction: null,
                    description: LocalizationService.GetString("OLLAMA_NOT_INSTALLED_DESC"),
                    actionButtonOnly: true
                );
                break;
        }
    }

    /// <summary>
    /// Ensures a model is selected when the available models collection changes.
    /// </summary>
    private void AvailableModels_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(SelectedModelName) && AvailableModels.Count > 0)
        {
            SelectedModelName = AvailableModels[0];
        }
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// Disposes of the view model, unregistering events and messages.
    /// </summary>
    public void Dispose()
    {
        AvailableModels.CollectionChanged -= AvailableModels_CollectionChanged;
        _ollamaService.StatusChanged -= OllamaServiceStatusChanged;
        _messenger.UnregisterAll(this);
    }

    #endregion
}
