// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

using Avalonia.Controls;
using Avalonia.Controls.Templates;
using avallama.ViewModels;
using avallama.Views;

namespace avallama.DataTemplates;

public class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        return param switch
        {
            WelcomeViewModel viewModel => CreateView(new WelcomeView(), viewModel),
            HomeViewModel viewModel => CreateView(new HomeView(), viewModel),
            OnboardingViewModel viewModel => CreateView(new OnboardingView(), viewModel),
            SettingsViewModel viewModel => CreateView(new SettingsView(), viewModel),
            ModelManagerViewModel viewModel => CreateView(new ModelManagerView(), viewModel),
            ScraperViewModel viewModel => CreateView(new ScraperView(), viewModel),
            _ => null
        };
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }

    private static Control CreateView(Control view, object viewModel)
    {
        view.DataContext = viewModel;
        return view;
    }
}
