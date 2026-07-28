// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

using System.Threading.Tasks;
using avallama.Constants.Application;
using avallama.Constants.Keys;
using avallama.Factories;
using avallama.Services;
using avallama.Services.Persistence;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace avallama.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    // PageFactory which can reach the delegate created in App.axaml.cs, i.e. returns the given PageViewModel
    private readonly PageFactory _pageFactory;
    private readonly ConfigurationService _configurationService;
    private readonly IModelCacheService _modelCacheService;
    private readonly IDialogService _dialogService;
    private readonly IMessenger _messenger;

    private string? _firstTime;

    [ObservableProperty] private PageViewModel? _currentPageViewModel;

    public MainViewModel(
        PageFactory pageFactory,
        ConfigurationService configurationService,
        IModelCacheService modelCacheService,
        IDialogService dialogService,
        IMessenger messenger
    )
    {
        _pageFactory = pageFactory;
        _configurationService = configurationService;
        _modelCacheService = modelCacheService;
        _dialogService = dialogService;
        _messenger = messenger;

        _messenger.Register<ApplicationMessage.RequestPage>(this, (_, msg) =>
        {
            CurrentPageViewModel = _pageFactory.GetPageViewModel(msg.Page);
        });

        _firstTime = _configurationService.ReadSetting(ConfigurationKey.FirstTime);
        if (string.IsNullOrEmpty(_firstTime))
        {
            CurrentPageViewModel = _pageFactory.GetPageViewModel(ApplicationPage.Welcome);
        }
        else if (_firstTime == "false")
        {
            CurrentPageViewModel = _pageFactory.GetPageViewModel(ApplicationPage.Home);
            CheckOllamaStart();
        }
    }

    private void CheckOllamaStart()
    {
        _firstTime = _configurationService.ReadSetting(ConfigurationKey.FirstTime);
        var apiHost = _configurationService.ReadSetting(ConfigurationKey.ApiHost);
        var apiPort = _configurationService.ReadSetting(ConfigurationKey.ApiPort);

        if (!string.IsNullOrEmpty(_firstTime) && !string.IsNullOrEmpty(apiHost) &&
            !string.IsNullOrEmpty(apiPort)) return;

        _configurationService.SaveSetting(ConfigurationKey.FirstTime, "false");

        // if the user is new, we request the AppService to show its all-knowing dialog
        _messenger.Send(new ApplicationMessage.AskOllamaStart());
    }

    [RelayCommand]
    public void OpenHome()
    {
        CheckOllamaStart();
        CurrentPageViewModel = _pageFactory.GetPageViewModel(ApplicationPage.Home);
    }

    [RelayCommand]
    public void OpenGuide()
    {
        CurrentPageViewModel = _pageFactory.GetPageViewModel(ApplicationPage.Guide);
    }

    [RelayCommand]
    public async Task OpenModelManager()
    {
        CurrentPageViewModel = _pageFactory.GetPageViewModel(ApplicationPage.ModelManager);
        var models = await _modelCacheService.GetCachedModelsAsync();
        var isScrapeDialogHandled = _configurationService.ReadSetting(ConfigurationKey.InitialScrapeAskDialogHandled);
        if (models.Count == 0 && (string.IsNullOrEmpty(isScrapeDialogHandled) || isScrapeDialogHandled == "False"))
        {
            // ask the user if they want to fetch all models from ollama's website if they haven't been asked before
            var dialogResult = await _dialogService.ShowConfirmationDialogAsync(
                LocalizationService.GetString("SCRAPE_ASK_DIALOG_TITLE"),
                LocalizationService.GetString("SCRAPE_ASK_DIALOG_POSITIVE"),
                LocalizationService.GetString("NOT_NOW"),
                LocalizationService.GetString("SCRAPE_ASK_DIALOG_DESCRIPTION"),
                ConfirmationType.Positive
            );

            switch (dialogResult)
            {
                case ConfirmationResult { Confirmation: ConfirmationType.Positive }:
                    // start the scraping process
                    CurrentPageViewModel = _pageFactory.GetPageViewModel(ApplicationPage.Scraper);
                    _configurationService.SaveSetting(ConfigurationKey.InitialScrapeAskDialogHandled, "True");
                    return;
                case ConfirmationResult { Confirmation: ConfirmationType.Negative }:
                    // inform the user that they can update the library in the Settings later
                    _dialogService.ShowInfoDialog(LocalizationService.GetString("SCRAPE_ASK_DIALOG_INFO"));
                    _configurationService.SaveSetting(ConfigurationKey.InitialScrapeAskDialogHandled, "True");
                    break;
            }
        }
    }

    [RelayCommand]
    public void OpenSettings()
    {
        CurrentPageViewModel = _pageFactory.GetPageViewModel(ApplicationPage.Settings);
    }

    [RelayCommand]
    public void StartScraper()
    {
        CurrentPageViewModel = _pageFactory.GetPageViewModel(ApplicationPage.Scraper);
    }
}
