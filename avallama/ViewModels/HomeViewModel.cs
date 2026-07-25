// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
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

/// <summary>
/// ViewModel for the Home page, managing chat conversations, model selection, and interactions with the Ollama service.
/// </summary>
public partial class HomeViewModel : PageViewModel
{
    #region Dependencies & Fields

    private readonly IOllamaService _ollamaService;
    private readonly IDialogService _dialogService;
    private readonly IConfigurationService _configurationService;
    private readonly IConversationService _conversationService;
    private readonly IUpdateService _updateService;
    private readonly IModelCacheService _modelCacheService;
    private readonly IMessenger _messenger;

    private bool _isInitializedAsync;
    private IList<OllamaModel> _previousDownloadedModels = [];
    private TaskCompletionSource<bool> _isOllamaReady = new();

    private IList<Conversation> _conversationsData = [];
    private readonly List<ConversationViewModel> _conversationViewModelsData = [];

    #endregion

    #region Observable Properties

    /// <summary>
    /// Gets or sets the text of the search box and filters conversations when changed.
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
    /// Gets the collection of available Ollama models.
    /// </summary>
    public ObservableCollection<string> AvailableModels { get; } = [];

    /// <summary>
    /// Gets the collection of conversation view models for the UI.
    /// </summary>
    public ObservableCollection<ConversationViewModel> ConversationViewModels { get; } = [];

    [ObservableProperty] private ConversationViewModel? _selectedConversationViewModel;

    [ObservableProperty] private ConversationViewModel? _activeConversationViewModel;

    // ui visibility & state flags
    [ObservableProperty] private bool _isRetryPanelVisible;
    [ObservableProperty] private bool _isRetryButtonVisible;
    [ObservableProperty] private string _retryInfoText = string.Empty;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="HomeViewModel"/> class.
    /// </summary>
    /// <param name="ollamaService">The service for Ollama interactions.</param>
    /// <param name="dialogService">The service for displaying dialogs.</param>
    /// <param name="configurationService">The service for application settings.</param>
    /// <param name="conversationService">The service for conversation interactions.</param>
    /// <param name="updateService">The service for checking application updates.</param>
    /// <param name="modelCacheService">The service for caching models.</param>
    /// <param name="messenger">The messenger for cross-component communication.</param>
    public HomeViewModel(
        IOllamaService ollamaService,
        IDialogService dialogService,
        IConfigurationService configurationService,
        IConversationService conversationService,
        IUpdateService updateService,
        IModelCacheService modelCacheService,
        IMessenger messenger)
    {
        Page = ApplicationPage.Home;

        _ollamaService = ollamaService;
        _dialogService = dialogService;
        _configurationService = configurationService;
        _conversationService = conversationService;
        _updateService = updateService;
        _modelCacheService = modelCacheService;
        _messenger = messenger;

        _ollamaService.StatusChanged += OllamaServiceStatusChanged;

        _messenger.Register<ApplicationMessage.ConversationBump>(this, (_, m) => BumpConversationToTop(m.ViewModel));
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
                {
                    await CheckForUpdatesAsync();
                }

                // get all downloaded models from cache database (with only their names, lightweight operation)
                if (_previousDownloadedModels.Count == 0)
                {
                    _previousDownloadedModels = await _modelCacheService.GetDownloadedModelsAsync();
                }
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
    /// Creates a new empty conversation and selects it.
    /// </summary>
    [RelayCommand]
    public async Task CreateNewConversation()
    {
        if (!string.IsNullOrEmpty(SearchBoxText))
        {
            SearchBoxText = string.Empty;
        }

        var newConversation = new Conversation(
            LocalizationService.GetString("NEW_CONVERSATION"),
            string.Empty
        );

        newConversation.Id = await _conversationService.CreateConversation(newConversation);
        _conversationsData.Add(newConversation);

        var newConversationViewModel = CreateNewConversationViewModel(newConversation);
        _conversationViewModelsData.Add(newConversationViewModel);
        ConversationViewModels.Insert(0, newConversationViewModel);
        SelectedConversationViewModel = newConversationViewModel;
    }

    /// <summary>
    /// Deletes the specified conversation after user confirmation.
    /// </summary>
    /// <param name="parameter">The unique identifier of the conversation to delete.</param>
    [RelayCommand]
    public async Task DeleteConversation(object parameter)
    {
        if (parameter is not Guid guid || SelectedConversationViewModel == null ||
            ConversationViewModels.Count == 0) return;

        var res = await _dialogService.ShowConfirmationDialogAsync(
            LocalizationService.GetString("CONFIRM_DELETION_DIALOG_TITLE"),
            LocalizationService.GetString("DELETE"),
            LocalizationService.GetString("CANCEL"),
            string.Format(LocalizationService.GetString("CONFIRM_DELETION_DIALOG_DESC"),
                LocalizationService.GetString("THIS_CONVERSATION")),
            ConfirmationType.Positive);

        if (res is ConfirmationResult { Confirmation: ConfirmationType.Negative }) return;

        var conversationViewModel = ConversationViewModels.First(cvm => cvm.Conversation.Id == guid);

        ConversationViewModels.Remove(conversationViewModel);
        _conversationViewModelsData.Remove(conversationViewModel);
        _conversationsData.Remove(conversationViewModel.Conversation);

        await _conversationService.DeleteConversation(guid);

        if (_conversationsData.Count == 0 && _conversationViewModelsData.Count == 0)
        {
            await CreateNewConversation();
        }

        switch (ConversationViewModels.Count)
        {
            case > 0:
                SelectedConversationViewModel = ConversationViewModels[0];
                break;
            case 0 when _conversationViewModelsData.Count > 0:
                SelectedConversationViewModel = _conversationViewModelsData[0];
                break;
        }
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Initializes the conversation list from the database. This is called only once.
    /// </summary>
    private async Task InitializeConversations()
    {
        _conversationsData = await _conversationService.GetConversations();

        if (_conversationsData is not { Count: > 0 })
        {
            await CreateNewConversation();
            return;
        }

        foreach (var conversation in _conversationsData)
        {
            var conversationViewModel = CreateNewConversationViewModel(conversation);
            _conversationViewModelsData.Add(conversationViewModel);
            ConversationViewModels.Add(conversationViewModel);
        }

        SelectedConversationViewModel = ConversationViewModels[0];
    }

    /// <summary>
    /// Initializes available models by querying the Ollama service.
    /// </summary>
    private async Task InitializeModels()
    {
        // cancel the previous connection waiting task and initialize a new one if it's completed or the api was connected
        if (_isOllamaReady.Task.IsCompleted ||
            _ollamaService.CurrentServiceStatus.ServiceState == OllamaServiceState.Ready)
        {
            _isOllamaReady.TrySetCanceled();
            _isOllamaReady = new TaskCompletionSource<bool>();
        }
        else
        {
            // waits for the ollama api connection
            await _isOllamaReady.Task;
        }

        // get all downloaded models from ollama api (with only their names)
        var downloadedModels = await _ollamaService.GetDownloadedModelsAsync();

        // compare model names that were set in the previous initialization and model names coming from the api
        var currentNames = downloadedModels.Select(m => m.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var previousNames = _previousDownloadedModels.Select(m => m.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var hasChanges = !currentNames.SetEquals(previousNames);

        // if the names differ, mark deleted models as deleted in cache db and upsert downloaded models with all info
        if (hasChanges)
        {
            // deleted models
            var deletedModels = _previousDownloadedModels.Where(m => !currentNames.Contains(m.Name)).ToList();

            foreach (var deletedModel in deletedModels)
            {
                deletedModel.IsDownloaded = false;
                await _modelCacheService.UpdateModelAsync(deletedModel);
            }

            // newly downloaded models (either downloaded from the app or outside with ollama cli)
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

        AvailableModels.Clear();

        if (downloadedModels.Count > 0)
        {
            foreach (var model in downloadedModels)
            {
                AvailableModels.Add(model.Name);
            }
        }
    }

    /// <summary>
    /// Moves the specified conversation to the top of the lists.
    /// </summary>
    /// <param name="viewModel">The conversation view model to move.</param>
    private void BumpConversationToTop(ConversationViewModel viewModel)
    {
        _conversationViewModelsData.Remove(viewModel);
        _conversationViewModelsData.Insert(0, viewModel);

        ConversationViewModels.Remove(viewModel);
        ConversationViewModels.Insert(0, viewModel);

        SelectedConversationViewModel = viewModel;
    }

    /// <summary>
    /// Checks if a new version of the application is available.
    /// Shows a dialog prompting the user to visit the GitHub releases page if an update is found.
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
        if (_conversationViewModelsData.Count == 0) return;

        var search = SearchBoxText.Trim();
        ConversationViewModels.Clear();

        if (string.IsNullOrEmpty(search))
        {
            foreach (var vm in _conversationViewModelsData)
            {
                ConversationViewModels.Add(vm);
            }
        }
        else
        {
            var filteredVms = _conversationViewModelsData
                .Select(vm => new
                {
                    ViewModel = vm,
                    Score = SearchUtilities.CalculateMatchScore(vm.Conversation.Title, search)
                })
                .Where(x => x.Score >= 25)
                .OrderByDescending(x => x.Score)
                .Select(x => x.ViewModel);

            foreach (var vm in filteredVms)
            {
                ConversationViewModels.Add(vm);
            }
        }

        if (ActiveConversationViewModel != null && ConversationViewModels.Contains(ActiveConversationViewModel))
        {
            SelectedConversationViewModel = ActiveConversationViewModel;
        }
    }

    /// <summary>
    /// Creates a new <see cref="ConversationViewModel"/> instance for the given conversation.
    /// </summary>
    /// <param name="conversation">The conversation model.</param>
    /// <returns>A new conversation view model.</returns>
    private ConversationViewModel CreateNewConversationViewModel(Conversation conversation)
    {
        return new ConversationViewModel(
            conversation,
            _ollamaService,
            _dialogService,
            _configurationService,
            _conversationService,
            _messenger,
            AvailableModels
        );
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// Handles changes to the selected conversation view model.
    /// </summary>
    /// <param name="value">The new selected view model.</param>
    partial void OnSelectedConversationViewModelChanged(ConversationViewModel? value)
    {
        if (value != null)
        {
            ActiveConversationViewModel = value;
        }
    }

    /// <summary>
    /// Handles changes to the active conversation view model, triggering its initialization.
    /// </summary>
    /// <param name="value">The new active view model.</param>
    partial void OnActiveConversationViewModelChanged(ConversationViewModel? value)
    {
        if (value != null)
        {
            _ = value.InitializeAsync();
        }
    }

    /// <summary>
    /// Handles changes in the Ollama service status.
    /// </summary>
    /// <param name="status">The updated status of the service.</param>
    private void OllamaServiceStatusChanged(OllamaServiceStatus status)
    {
        if (status.ServiceState == OllamaServiceState.Ready)
        {
            _isOllamaReady.TrySetResult(true);
        }
    }

    #endregion
}
