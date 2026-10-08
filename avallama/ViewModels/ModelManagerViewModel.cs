// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using avallama.Constants;
using avallama.Constants.Application;
using avallama.Constants.Keys;
using avallama.Constants.States;
using avallama.Models.Ollama;
using avallama.Services;
using avallama.Services.Ollama;
using avallama.Services.Persistence;
using avallama.Services.Queue;
using avallama.Utilities;
using avallama.Utilities.Network;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace avallama.ViewModels;

// TODO: handle OllamaService status changes and update UI accordingly

public partial class ModelManagerViewModel : PageViewModel
{
    private readonly IDialogService _dialogService;
    private readonly IOllamaService _ollamaService;
    private readonly IModelCacheService _modelCacheService;
    private readonly INetworkManager _networkManager;
    private readonly IConfigurationService _configurationService;
    private readonly IModelDownloadQueueService _modelDownloadQueueService;
    private readonly IMessenger _messenger;

    // all loaded models (stays in the memory)
    private IEnumerable<ModelItemViewModel> _modelItemViewModelsData = [];

    // filtered models
    private IEnumerable<ModelItemViewModel> _filteredModelItemViewModelsData = [];

    // model sorting options
    public IEnumerable<SortingOption> SortingOptions { get; } = Enum.GetValues<SortingOption>();

    // rendered models on the UI
    [ObservableProperty] private ObservableCollection<ModelItemViewModel> _modelItemViewModels = [];

    [ObservableProperty] private string _downloadedModelsInfo = string.Empty;
    [ObservableProperty] private bool _hasDownloadedModels;
    [ObservableProperty] private bool _hasModelsToDisplay;
    [ObservableProperty] private ModelItemViewModel? _selectedModelItemViewModel;
    [ObservableProperty] private bool _isModelInfoBlockVisible;
    [ObservableProperty] private bool _isPaginationButtonVisible;

    public const int PaginationLimit = 50;
    private int _paginationIndex;

    public SortingOption SelectedSortingOption
    {
        get;
        set
        {
            field = value;
            OnPropertyChanged();
            SortModels();
        }
    }

    public string SearchBoxText
    {
        get;
        set
        {
            field = value;
            OnPropertyChanged();
            FilterModels();
        }
    } = string.Empty;

    public ModelManagerViewModel(
        IDialogService dialogService,
        IOllamaService ollamaService,
        IModelCacheService modelCacheService,
        INetworkManager networkManager,
        IConfigurationService configurationService,
        IModelDownloadQueueService modelDownloadQueueService,
        IMessenger messenger)
    {
        Page = ApplicationPage.ModelManager;
        _dialogService = dialogService;
        _ollamaService = ollamaService;
        _modelCacheService = modelCacheService;
        _networkManager = networkManager;
        _configurationService = configurationService;
        _modelDownloadQueueService = modelDownloadQueueService;
        _messenger = messenger;

        _messenger.Register<ApplicationMessage.ModelStatusChanged>(this, (_, _) =>
        {
            Dispatcher.UIThread.Invoke(() =>
            {
                UpdateDownloadedModelsInfo();
                SortModels();
            });
        });
    }

    [RelayCommand]
    public async Task InitializeAsync()
    {
        // if there are loaded models skip, but update parallelism settings
        if (_modelItemViewModelsData.Any())
        {
            ApplyParallelismSettings();
            return;
        }

        await LoadModelsData();
        SelectedSortingOption = SortingOption.Downloaded;
    }

    private void ApplyParallelismSettings()
    {
        var isParallelDownloadEnabled = _configurationService.ReadSetting(ConfigurationKey.IsParallelDownloadEnabled);
        var parallelism = isParallelDownloadEnabled == "True" ? 5 : 1;
        _modelDownloadQueueService.SetParallelism(parallelism);
    }

    private async Task LoadModelsData()
    {
        // Get models data from cache excluding cloud models
        var rawModels = (await _modelCacheService.GetCachedModelsAsync())
            .Where(m => !m.Name.EndsWith("-cloud", StringComparison.OrdinalIgnoreCase));

        _modelItemViewModelsData = rawModels.Select(CreateModelItemViewModel).ToList();

        var modelItemsData = _modelItemViewModelsData.ToList();
        IsPaginationButtonVisible = modelItemsData.Count > PaginationLimit;

        _paginationIndex = Math.Min(modelItemsData.Count, PaginationLimit);

        if (modelItemsData.Count == 0) return;

        SortModels();

        ModelItemViewModels = new ObservableCollection<ModelItemViewModel>(modelItemsData.Take(Math.Min(modelItemsData.Count, PaginationLimit)));
        HasModelsToDisplay = ModelItemViewModels.Count > 0;

        var downloadedModelsCount = modelItemsData.Count(m => m.Model.IsDownloaded);

        HasDownloadedModels = downloadedModelsCount > 0;

        if (!HasDownloadedModels) return;

        UpdateDownloadedModelsInfo();
    }

    private ModelItemViewModel CreateModelItemViewModel(OllamaModel model)
    {
        var vm = new ModelItemViewModel(
            model,
            _ollamaService,
            _modelDownloadQueueService,
            _dialogService,
            _modelCacheService,
            _messenger
        );
        return vm;
    }

    private void SortModels()
    {
        var search = SearchBoxText.Trim();
        var hasSearch = !string.IsNullOrEmpty(search);
        var dataToSort = hasSearch ? _filteredModelItemViewModelsData : _modelItemViewModelsData;
        var modelItemList = dataToSort.ToList();

        if (modelItemList.Count == 0) return;

        // active downloads first then the rest of the list
        var activeDownloads = modelItemList
            .OrderByDescending(vm => vm.DownloadRequest != null);

        IEnumerable<ModelItemViewModel> sortResult;

        switch (SelectedSortingOption)
        {
            // if there is no model in downloaded status then it returns the normal one
            // if there is then it takes the downloaded ones separately from the rest and then concatenates them with a Models that has the Downloaded ones removed
            // thus the downloaded ones will be in the front
            case SortingOption.Downloaded:
                sortResult = activeDownloads.ThenByDescending(vm => vm.Model.IsDownloaded);
                break;
            case SortingOption.PullCountAscending:
                sortResult = activeDownloads.ThenBy(vm => vm.Model.Family?.PullCount);
                break;
            case SortingOption.PullCountDescending:
                sortResult = activeDownloads.ThenByDescending(vm => vm.Model.Family?.PullCount);
                break;
            case SortingOption.SizeAscending:
                sortResult = activeDownloads.ThenBy(vm => vm.Model.Size);
                break;
            case SortingOption.SizeDescending:
                sortResult = activeDownloads.ThenByDescending(vm => vm.Model.Size);
                break;
            default:
                sortResult = activeDownloads.ThenBy(vm => vm.Model.Name);
                break;
        }

        if (hasSearch)
        {
            _filteredModelItemViewModelsData = sortResult;
        }
        else
        {
            _modelItemViewModelsData = sortResult;
        }

        ResetPagination(hasSearch ? _filteredModelItemViewModelsData : _modelItemViewModelsData);
    }

    // Filters models by the search text and sets the _filteredModelsData
    private void FilterModels()
    {
        if (!_modelItemViewModelsData.Any())
        {
            HasModelsToDisplay = false;
            return;
        }

        var search = SearchBoxText.Trim();
        if (string.IsNullOrEmpty(search))
        {
            ResetPagination(_modelItemViewModelsData);
            return;
        }

        _filteredModelItemViewModelsData = _modelItemViewModelsData
            .Select(vm => new
            {
                ViewModel = vm,
                Score = SearchUtilities.CalculateMatchScore(vm.Model.Name, search)
            })
            .Where(x => x.Score >= 25)
            .OrderByDescending(x => x.Score)
            .Select(x => x.ViewModel);

        var filteredModelsData = _filteredModelItemViewModelsData.ToList();

        ResetPagination(filteredModelsData);

        HasModelsToDisplay = filteredModelsData.Count > 0;
    }

    private void ResetPagination(IEnumerable<ModelItemViewModel> modelItems)
    {
        var modelsList = modelItems.ToList();
        ModelItemViewModels = new ObservableCollection<ModelItemViewModel>(modelsList.Take(Math.Min(PaginationLimit, modelsList.Count)));
        _paginationIndex = Math.Min(PaginationLimit, modelsList.Count);
        IsPaginationButtonVisible = modelsList.Count > PaginationLimit;
    }

    [RelayCommand]
    public async Task ForwardToOllama(object parameter)
    {
        if (!await _networkManager.IsInternetAvailableAsync())
        {
            _dialogService.ShowErrorDialog(LocalizationService.GetString("Common.Network.NoConnection"), false);
            return;
        }

        if (parameter is string modelName)
        {
            const string libraryUrl = @"https://ollama.com/library/";

            Process.Start(new ProcessStartInfo
            {
                FileName = libraryUrl + modelName,
                UseShellExecute = true
            });
        }
    }

    [RelayCommand]
    public void Paginate()
    {
        var search = SearchBoxText.Trim();
        var hasSearch = !string.IsNullOrEmpty(search);
        var dataToPaginate = hasSearch ? _filteredModelItemViewModelsData.ToList() : _modelItemViewModelsData.ToList();

        if (dataToPaginate.Count == 0) return;

        if (_paginationIndex < dataToPaginate.Count)
        {
            var modelsToAdd = Math.Min(PaginationLimit, dataToPaginate.Count - _paginationIndex);
            foreach (var model in dataToPaginate.Skip(_paginationIndex).Take(modelsToAdd))
            {
                ModelItemViewModels.Add(model);
                _paginationIndex++;
            }
        }

        IsPaginationButtonVisible = _paginationIndex < dataToPaginate.Count;
    }

    private void UpdateDownloadedModelsInfo()
    {
        var downloadedModelsCount = _modelItemViewModelsData.Count(vm => vm.Model.IsDownloaded);
        var downloadedSizeSum = _modelItemViewModelsData
            .Where(vm => vm.Model.IsDownloaded)
            .Sum(vm => vm.Model.Size);

        DownloadedModelsInfo =
            string.Format(
                LocalizationService.GetString("Models.List.DownloadedCount"),
                downloadedModelsCount
            );

        DownloadedModelsInfo += $" ({ConversionHelper.BytesToReadableSize(downloadedSizeSum)})";
    }

    [RelayCommand]
    public void SelectModel(object parameter)
    {
        if (parameter is string modelName)
        {
            if (string.IsNullOrEmpty(modelName)) return;

            IsModelInfoBlockVisible = true;

            var modelItemViewModelFromName = ModelItemViewModels.FirstOrDefault(vm => vm.Model.Name == modelName);
            SelectedModelItemViewModel = modelItemViewModelFromName;
        }
    }

    [RelayCommand]
    public void ShowInfo()
    {
        _dialogService.ShowInfoDialog(LocalizationService.GetString("Models.Guide.Description"));
    }
}
