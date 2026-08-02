// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

using System.Collections.Generic;
using avallama.Constants.Application;
using avallama.Constants.Keys;
using avallama.Factories;
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

    private readonly Stack<ApplicationPage> _navigationStack = new();

    [ObservableProperty] private PageViewModel _currentPageViewModel;

    public MainViewModel(
        PageFactory pageFactory,
        ConfigurationService configurationService,
        IMessenger messenger
    )
    {
        _pageFactory = pageFactory;
        _configurationService = configurationService;

        messenger.Register<ApplicationMessage.NavigateToPage>(this,
            (_, msg) => { NavigateTo(msg.Page); });

        messenger.Register<ApplicationMessage.NavigateBack>(this, (_, _) => NavigateBack());

        var onboardingCompleted = configurationService.ReadSetting(ConfigurationKey.OnboardingCompleted);

        CurrentPageViewModel = _pageFactory.GetPageViewModel(string.IsNullOrEmpty(onboardingCompleted)
            ? ApplicationPage.Welcome
            : ApplicationPage.Home);
    }

    [RelayCommand]
    public void NavigateTo(object parameter)
    {
        if (parameter is not ApplicationPage page) return;

        // pushes current page to stack (which is a navigation history) so we can pop it to go back
        _navigationStack.Push(CurrentPageViewModel.Page);

        var onboardingCompleted = _configurationService.ReadSetting(ConfigurationKey.OnboardingCompleted);

        // if we receive 'Home' as page to navigate and the onboarding flag is empty
        // that means the user finished onboarding, either skipped scraping or finished it
        // so we set the flag here
        if (string.IsNullOrEmpty(onboardingCompleted) && page is ApplicationPage.Home)
        {
            _configurationService.SaveSetting(ConfigurationKey.OnboardingCompleted, "True");
        }

        CurrentPageViewModel = _pageFactory.GetPageViewModel(page);
    }

    [RelayCommand]
    public void NavigateBack()
    {
        if (_navigationStack.Count == 0) return;

        var page = _navigationStack.Pop();
        CurrentPageViewModel = _pageFactory.GetPageViewModel(page);
    }
}
