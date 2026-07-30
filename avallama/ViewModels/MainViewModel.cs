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
    private readonly IMessenger _messenger;

    private Stack<ApplicationPage> _navigationStack = new();

    private string? _onboardingCompleted;

    [ObservableProperty] private PageViewModel? _currentPageViewModel;

    public MainViewModel(
        PageFactory pageFactory,
        ConfigurationService configurationService,
        IMessenger messenger
    )
    {
        _pageFactory = pageFactory;
        _configurationService = configurationService;
        _messenger = messenger;

        _messenger.Register<ApplicationMessage.NavigateToPage>(this,
            (_, msg) => { NavigateTo(msg.Page); });

        _messenger.Register<ApplicationMessage.NavigateBack>(this, (_, _) => NavigateBack());

        _onboardingCompleted = _configurationService.ReadSetting(ConfigurationKey.OnboardingCompleted);
        if (string.IsNullOrEmpty(_onboardingCompleted))
        {
            CurrentPageViewModel = _pageFactory.GetPageViewModel(ApplicationPage.Welcome);
        }
        else if (_onboardingCompleted == "false")
        {
            CurrentPageViewModel = _pageFactory.GetPageViewModel(ApplicationPage.Home);
        }
    }

    [RelayCommand]
    public void NavigateTo(object parameter)
    {
        if (parameter is not ApplicationPage page) return;

        // should never be null tho
        if (CurrentPageViewModel != null)
        {
            _navigationStack.Push(CurrentPageViewModel.Page);
        }

        if (string.IsNullOrEmpty(_onboardingCompleted) && page == ApplicationPage.Home)
        {
            _configurationService.SaveSetting(ConfigurationKey.OnboardingCompleted, "false");
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
