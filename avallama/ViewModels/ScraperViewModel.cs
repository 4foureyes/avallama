// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using avallama.Constants.Application;
using avallama.Constants.Keys;
using avallama.Models.Ollama;
using avallama.Services;
using avallama.Services.Ollama;
using avallama.Services.Persistence;
using avallama.Utilities.Network;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace avallama.ViewModels;

public partial class ScraperViewModel : PageViewModel
{
    private readonly IOllamaService _ollamaService;
    private readonly IModelCacheService _modelCacheService;
    private readonly IDialogService _dialogService;
    private readonly IConfigurationService _configurationService;
    private readonly IMessenger _messenger;
    private readonly INetworkManager _networkManager;

    private CancellationTokenSource? _cancellationTokenSource;
    private CancellationToken _cancellationToken;

    private int _receivedModels;

    [ObservableProperty] private string _progressText = string.Empty;
    [ObservableProperty] private bool _isCancelEnabled = true;

    public ScraperViewModel(
        IOllamaService ollamaService,
        IModelCacheService modelCacheService,
        IDialogService dialogService,
        IConfigurationService configurationService,
        IMessenger messenger,
        INetworkManager networkManager
    )
    {
        Page = ApplicationPage.Scraper;
        _ollamaService = ollamaService;
        _modelCacheService = modelCacheService;
        _dialogService = dialogService;
        _configurationService = configurationService;
        _messenger = messenger;
        _networkManager = networkManager;
    }

    [RelayCommand]
    public async Task InitializeAsync()
    {
        if (!await _networkManager.IsInternetAvailableAsync())
        {
            _dialogService.ShowErrorDialog(LocalizationService.GetString("NO_INTERNET_CONNECTION"), false);
            CancelScraping();
            return;
        }
        await ScrapeModels();
    }

    [RelayCommand]
    public void CancelScraping()
    {
        _cancellationTokenSource?.Cancel();
        _messenger.Send(new ApplicationMessage.NavigateBack());
    }

    private async Task ScrapeModels()
    {
        try
        {
            _cancellationTokenSource = new CancellationTokenSource();
            _cancellationToken = _cancellationTokenSource.Token;

            var monitorTask = MonitorInternetAsync(_cancellationToken);

            var models = new List<OllamaModel>();
            await foreach (var model in _ollamaService.StreamAllScrapedModelsAsync(_cancellationToken))
            {
                _receivedModels++;

                ProgressText = string.Format(
                    LocalizationService.GetString("SCRAPER_MODELS_FOUND"),
                    _receivedModels
                );

                models.Add(model);
            }

            ProgressText = LocalizationService.GetString("SCRAPER_CACHING_MODELS");

            // This "calls" the scraper again, but the result is cached in OllamaService
            var families = await _ollamaService.GetScrapedFamiliesAsync();
            await _modelCacheService.CacheModelFamilyAsync(families);
            await _modelCacheService.CacheModelsAsync(models);

            _dialogService.ShowInfoDialog(LocalizationService.GetString("SCRAPING_FINISHED_DESC"));
            _configurationService.SaveSetting(ConfigurationKey.LastUpdatedCache,
                DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            _messenger.Send(new ApplicationMessage.NavigateBack());

            await monitorTask;
        }
        catch (OperationCanceledException)
        {
            // TODO: proper logging
        }
        catch (Exception)
        {
            // TODO: proper logging
        }
    }

    private async Task MonitorInternetAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(3), token);

                if (!await _networkManager.IsInternetAvailableAsync())
                {
                    _dialogService.ShowErrorDialog(LocalizationService.GetString("LOST_INTERNET_CONNECTION"), false);
                    CancelScraping();
                    return;
                }
            }
        }
        catch (OperationCanceledException) { }
    }
}
