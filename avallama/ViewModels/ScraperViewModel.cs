// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using avallama.Constants;
using avallama.Models;
using avallama.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace avallama.ViewModels;

public partial class ScraperViewModel : PageViewModel
{
    private IOllamaService _ollamaService;
    private IModelCacheService _modelCacheService;
    private IDialogService _dialogService;

    private int _receivedModels;

    [ObservableProperty] private string _progressText = string.Empty;

    public ScraperViewModel(
        IOllamaService ollamaService,
        IModelCacheService modelCacheService,
        IDialogService dialogService
    )
    {
        Page = ApplicationPage.Scraper;
        _ollamaService = ollamaService;
        _modelCacheService = modelCacheService;
        _dialogService = dialogService;
    }

    public async Task InitializeAsync()
    {
        await ScrapeModels();
    }

    private async Task ScrapeModels()
    {
        try
        {
            var models = new List<OllamaModel>();
            await foreach (var model in _ollamaService.StreamAllScrapedModelsAsync())
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
            // TODO: befejezni, dialogot megjeleníteni és dialog actionnél esetleg usert átvinni HomeView-be
            ProgressText = "done";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error while loading the models data: {ex.Message}");
            // TODO: proper logging
        }
    }
}
