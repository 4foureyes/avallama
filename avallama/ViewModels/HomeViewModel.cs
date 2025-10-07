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
using avallama.Constants;
using avallama.Models;
using avallama.Services;
using avallama.Utilities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace avallama.ViewModels;

public partial class HomeViewModel : PageViewModel
{
    private const string OllamaDownloadUrl = @"https://ollama.com/download/";

    public string LanguageLimitationWarning { get; } =
        string.Format(LocalizationService.GetString("ONLY_SUPPORTED_MODEL"), "llama3.2");

    public string ResourceLimitWarning { get; } = string.Format(LocalizationService.GetString("LOW_VRAM_WARNING"));

    public string NoModelsDownloadedWarning { get; } =
        string.Format(LocalizationService.GetString("NOT_DOWNLOADED_WARNING"));

    private readonly IOllamaService _ollamaService;
    private readonly IDialogService _dialogService;
    private readonly IConfigurationService _configurationService;
    private readonly IDatabaseService _databaseService;

    private readonly TaskCompletionSource<bool> _ollamaServerStarted = new();
    private readonly IMessenger _messenger;

    public string ScrollSetting = string.Empty;

    private ObservableStack<Conversation> _conversations = null!;

    public ObservableStack<Conversation> Conversations
    {
        get => _conversations;
        set => SetProperty(ref _conversations, value);
    }
    private readonly ConditionalWeakTable<Conversation, ConversationState> _conversationStates = new();

    private ObservableCollection<string> _availableModels;

    public ObservableCollection<string> AvailableModels
    {
        get => _availableModels;
        set => SetProperty(ref _availableModels, value);
    }

    [ObservableProperty] private string _newMessageText = string.Empty;
    [ObservableProperty] private bool _isResourceWarningVisible;
    [ObservableProperty] private bool _isNoModelsDownloadedVisible;
    [ObservableProperty] private bool _isDownloaded;
    [ObservableProperty] private string _currentlySelectedModel;
    [ObservableProperty] private bool _isDownloading;
    [ObservableProperty] private string _downloadStatus = "";
    [ObservableProperty] private double _downloadProgress;
    [ObservableProperty] private bool _isMaxPercent;
    [ObservableProperty] private string _downloadSpeed = string.Empty;
    [ObservableProperty] private string _downloadAmount = string.Empty;
    [ObservableProperty] private Conversation _selectedConversation = null!;
    [ObservableProperty] private string _remoteConnectionText = string.Empty;
    [ObservableProperty] private bool _isRemoteConnectionTextVisible;
    [ObservableProperty] private bool _isRetryPanelVisible;
    [ObservableProperty] private bool _isRetryButtonVisible;
    [ObservableProperty] private string _retryInfoText = string.Empty;
    [ObservableProperty] private bool _showInformationalMessages;
    [ObservableProperty] private bool _isModelsDropdownEnabled = true;

    // TODO:
    // Van egy bug amit kicsit nehezen lehet reprodukálni, de épp a 3. üzenetem írtam, generálta volna az új beszélgetés címet az ollama
    // majd átkattintottam gyors egy másik beszélgetésbe és annak a címébe vitte bele az új címet, hozzáfűzte
    // és az előző beszélgetés címe eltűnt, nem is tudtam belekattintani, ezt meg lehet csinálni többször, átviszi a generálást máshova
    // ezt majd vhogy javítani
    [RelayCommand]
    private async Task SendMessage()
    {
        if (NewMessageText.Length == 0) return;
        NewMessageText = NewMessageText.Trim();
        var message = new Message(NewMessageText);
        SelectedConversation.AddMessage(message);
        await _databaseService.InsertMessage(SelectedConversation.ConversationId, message, null, null);
        NewMessageText = string.Empty;
        if (Conversations.IndexOf(SelectedConversation) != 0)
        {
            Conversations.Remove(SelectedConversation);
            Conversations.Push(SelectedConversation);
        }
        await AddGeneratedMessage();
    }

    [RelayCommand]
    public async Task CreateNewConversation()
    {
        var newConversation = new Conversation(
            LocalizationService.GetString("NEW_CONVERSATION"),
            "llama3.2"
        );
        newConversation.ConversationId = await _databaseService.CreateConversation(newConversation);
        Conversations.Push(newConversation);
        SelectedConversation = newConversation;
    }

    [RelayCommand]
    public async Task SelectConversation(object parameter)
    {
        if (parameter is not Guid guid) return;

        if (guid == SelectedConversation.ConversationId) return;

        var selectedConversation = Conversations.FirstOrDefault(x => x.ConversationId == guid);
        if (selectedConversation == null) return;

        SelectedConversation = selectedConversation;

        var state = GetState(SelectedConversation);
        if (state.MessagesLoaded) return;

        SelectedConversation.Messages = await _databaseService.GetMessagesForConversation(SelectedConversation);
        state.MessagesLoaded = true;
    }

    [RelayCommand]
    public async Task RetryOllamaConnection()
    {
        await _ollamaService.Retry();
    }

    private async Task AddGeneratedMessage()
    {
        var generatedMessage = new GeneratedMessage("", 0.0);
        SelectedConversation.AddMessage(generatedMessage);
        var messageHistory = new List<Message>(SelectedConversation.Messages.ToList());
        messageHistory.RemoveAt(messageHistory.Count - 1);

        // hibás (nem generált) üzenetek kitörlése
        messageHistory.RemoveAll(message => message is FailedMessage);

        await foreach (var chunk in _ollamaService.GenerateMessage(messageHistory, CurrentlySelectedModel))
        {
            if (chunk.Message != null) generatedMessage.Content += chunk.Message.Content;

            if (chunk.EvalCount.HasValue && chunk.EvalDuration.HasValue)
            {
                double tokensPerSecond =
                    chunk.EvalCount.GetValueOrDefault() / (double)chunk.EvalDuration * Math.Pow(10, 9);
                generatedMessage.GenerationSpeed = tokensPerSecond;
                IsResourceWarningVisible = tokensPerSecond < 20;
                SelectedConversation.MessageCountToRegenerateTitle++;
                if (SelectedConversation.MessageCountToRegenerateTitle == 3)
                {
                    await RegenerateConversationTitle();
                }
            }
        }
        await _databaseService.InsertMessage(SelectedConversation.ConversationId, generatedMessage, CurrentlySelectedModel, generatedMessage.GenerationSpeed);
    }

    private async Task RegenerateConversationTitle()
    {
        if (await _ollamaService.IsOllamaServerRunning())
        {
            SelectedConversation.Title = string.Empty;
            const string request =
                "Generate only a single short title for this conversation with no use of quotation marks.";
            var tmpMessage = new Message(request);
            var messageHistory = new List<Message>(SelectedConversation.Messages.ToList()) { tmpMessage };
            await foreach (var chunk in _ollamaService.GenerateMessage(messageHistory, CurrentlySelectedModel))
            {
                if (chunk.Message != null) SelectedConversation.Title += chunk.Message.Content;
            }

            SelectedConversation.MessageCountToRegenerateTitle = 0;
            await _databaseService.UpdateConversationTitle(SelectedConversation);
        }
    }

    private async Task CheckModelDownload()
    {
        IsDownloaded = await _ollamaService.IsModelDownloaded();
        IsNoModelsDownloadedVisible = !IsDownloaded;
    }

    public HomeViewModel(
        IOllamaService ollamaService,
        IDialogService dialogService,
        IConfigurationService configurationService,
        IDatabaseService databaseService,
        IMessenger messenger
    )
    {
        // beállítás, hogy a viewmodel milyen paget kezel
        Page = ApplicationPage.Home;

        _dialogService = dialogService;
        _ollamaService = ollamaService;
        _configurationService = configurationService;
        _databaseService = databaseService;
        _messenger = messenger;
        _availableModels = [];
        _currentlySelectedModel = "";

        _messenger.Register<ApplicationMessage.ReloadSettings>(this, (_, _) => { LoadSettings(); });

        LoadSettings();

        _ollamaService.ServiceStatusChanged += OllamaServiceStatusChanged;
        if (_ollamaService.CurrentServiceStatus != null)
        {
            OllamaServiceStatusChanged(_ollamaService.CurrentServiceStatus, _ollamaService.CurrentServiceMessage);
        }

        // Yes I know this is also fire-and-forget but exceptions are "handled"
        _ = Task.Run(InitializePipelineAsync);
    }

    private async Task InitializePipelineAsync()
    {
        try
        {
            // Triggering conversations and ollama task in parallel
            var conversationsTasks = InitializeConversations();
            var ollamaTask  = InitializeOllama();

            await Task.WhenAll(conversationsTasks, ollamaTask);

            // Initialize models has hard dependency on ollama init, so we run after
            await InitializeModels();
        }
        catch (Exception ex)
        {
            //TODO implement logging service, console logged for now
            Console.WriteLine(ex);
        }
    }

    private async Task InitializeOllama()
    {
        // megvárja amíg kapcsolódik az ollama szerverhez, ez gondolom azért kell, mert hamarabb futna le ez a metódus mint hogy a szerver elindul (?)
        await _ollamaServerStarted.Task;

        // ezt majd dinamikusan aszerint hogy melyik modell van használatban betöltéskor
        await CheckModelDownload();
    }

    private void LoadSettings()
    {
        ScrollSetting = _configurationService.ReadSetting(ConfigurationKey.ScrollToBottom);
        ShowInformationalMessages =
            _configurationService.ReadSetting(ConfigurationKey.ShowInformationalMessages) == "True";
    }

    private void RedirectToOllamaDownload()
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

    private void OllamaServiceStatusChanged(ServiceStatus? status, string? message)
    {
        // TODO: logolni is majd
        switch (status)
        {
            case ServiceStatus.Running:
                var apiHost = _configurationService.ReadSetting(ConfigurationKey.ApiHost);
                var apiPort = _configurationService.ReadSetting(ConfigurationKey.ApiPort);

                // "távoli kapcsolat" szöveg megjelenítése ha az alkalmazás nem lokális ollamára csatlakozik
                if (!string.IsNullOrEmpty(apiHost) && apiHost != "localhost" && apiHost != "127.0.0.1")
                {
                    RemoteConnectionText = string.Format(LocalizationService.GetString("REMOTE_CONNECTION"),
                        apiHost + ":" + apiPort);
                    IsRemoteConnectionTextVisible = true;
                }
                else
                {
                    IsRemoteConnectionTextVisible = false;
                }

                if (_ollamaServerStarted.Task.Status != TaskStatus.RanToCompletion)
                    _ollamaServerStarted.SetResult(true);
                IsRetryPanelVisible = false;
                break;
            case ServiceStatus.NotInstalled:
                _dialogService.ShowActionDialog(
                    title: LocalizationService.GetString("OLLAMA_NOT_INSTALLED"),
                    actionButtonText: LocalizationService.GetString("DOWNLOAD"),
                    action: () =>
                    {
                        RedirectToOllamaDownload();

                        // üzenet az AppServicenek hogy zárja be az appot
                        _messenger.Send(new ApplicationMessage.Shutdown());
                    },
                    closeAction: () => { _messenger.Send(new ApplicationMessage.Shutdown()); },
                    description: LocalizationService.GetString("OLLAMA_NOT_INSTALLED_DESC"),
                    false
                );
                break;
            case ServiceStatus.Retrying:
                RetryInfoText = LocalizationService.GetString("CONNECTING");
                IsRetryPanelVisible = true;
                IsRetryButtonVisible = false;
                break;
            default:
                // ServiceStatus.Failed

                // ha az utolsó egy "generálásnak" szánt üzenet, akkor azt lecseréljük FailedMessagere
                if (SelectedConversation.Messages.Count > 0)
                {
                    if (SelectedConversation.Messages.Last() is GeneratedMessage generatedMessage)
                    {
                        SelectedConversation.Messages.Remove(generatedMessage);
                        SelectedConversation.Messages.Add(new FailedMessage());
                    }
                }

                RetryInfoText = message ?? LocalizationService.GetString("OLLAMA_CONNECTION_ERROR");
                IsRemoteConnectionTextVisible = false;
                IsRetryPanelVisible = true;
                IsRetryButtonVisible = true;
                break;
        }
    }

    private async Task InitializeConversations()
    {
        _conversations = await _databaseService.GetConversations();
        if (_conversations.Count <= 0)
        {
            await CreateNewConversation();
            return;
        }

        SelectedConversation = _conversations.FirstOrDefault()!;
        SelectedConversation.Messages = await _databaseService.GetMessagesForConversation(SelectedConversation);
        GetState(SelectedConversation).MessagesLoaded = true;
    }

    private ConversationState GetState(Conversation conversation)
    {
        return _conversationStates.GetOrCreateValue(conversation);
    }

    public async Task InitializeModels()
    {
        IsModelsDropdownEnabled = false;
        AvailableModels.Add(LocalizationService.GetString("LOADING_MODELS"));
        CurrentlySelectedModel = AvailableModels[0];
        var downloadedModels = await _ollamaService.ListDownloaded();
        if (downloadedModels.Count == 0)
        {
            AvailableModels.Clear();
            AvailableModels.Add(LocalizationService.GetString("NO_MODELS_FOUND"));
            CurrentlySelectedModel = AvailableModels[0];
            IsModelsDropdownEnabled = false;
            IsNoModelsDownloadedVisible = true;
            return;
        }
        foreach (var model in downloadedModels)
        {
            AvailableModels.Add(model.Name);
        }
        AvailableModels.RemoveAt(0);
        CurrentlySelectedModel = AvailableModels.LastOrDefault() ?? string.Empty;
        IsModelsDropdownEnabled = true;
    }
}

// Helper class to save the state of a conversation object, so that DB queries only happen when selecting a conversation
// that has not yet been selected, since once messages are loaded, they are available in memory in the viewmodel
internal class ConversationState
{
    public bool MessagesLoaded { get; set; }
}
