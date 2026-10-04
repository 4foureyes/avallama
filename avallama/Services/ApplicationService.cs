// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

using System;
using System.Diagnostics;
using System.IO;
using avallama.Constants.Application;
using avallama.Services.Persistence;
using avallama.ViewModels;
using avallama.Views;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;

namespace avallama.Services;

public interface IApplicationService
{
    void InitializeMainWindow();
    void Shutdown();
    void Restart();
}

// helper class for customizing application operations (start, stop)
public class ApplicationService : IApplicationService
{
    private bool _isMainWindowInitialized;
    private readonly DialogService _dialogService;
    private readonly MainViewModel _mainViewModel;
    private readonly ConfigurationService _configurationService;

    public ApplicationService(
        DialogService dialogService,
        MainViewModel mainViewModel,
        ConfigurationService configurationService,
        IMessenger messenger
    )
    {
        _dialogService = dialogService;
        _mainViewModel = mainViewModel;
        _configurationService = configurationService;
        messenger.Register<ApplicationMessage.Shutdown>(this, (_, _) => { Shutdown(); });
        messenger.Register<ApplicationMessage.Restart>(this, (_, _) => { Restart(); });
    }

    public void Shutdown()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;
        Dispatcher.UIThread.Post(() => { desktop.Shutdown(); });
    }

    public void Restart()
    {
#if DEBUG
        Shutdown();
        return;
#endif
        var processPath = Environment.ProcessPath;

        // start another instance of the app
        Process.Start(new ProcessStartInfo
        {
            FileName = processPath,
            UseShellExecute = false
        });

        Shutdown();
    }

    public void InitializeMainWindow()
    {
        if (_isMainWindowInitialized) return;
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;
        desktop.MainWindow = new MainWindow
        {
            DataContext = _mainViewModel
        };
        desktop.MainWindow.Show();
        _isMainWindowInitialized = true;
    }
}
