// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using avallama.Extensions;
using Avalonia.Markup.Xaml;
using avallama.Services;
using Avalonia.Controls;
using Avalonia.Styling;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace avallama;

public partial class App : Application
{
    private OllamaService? _ollamaService;
    private DialogService? _dialogService;
    private DatabaseInitService? _databaseInitService;
    public static SqliteConnection SharedDbConnection { get; private set; } = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Az összes dependency létrehozása és eltárolása egy ServiceCollectionben
        var collection = new ServiceCollection();
        collection.AddCommonServices();

        // ServiceProvider ami biztosítja a létrehozott dependencyket
        var services = collection.BuildServiceProvider();

        // nem lehet semmilyen dependency lekérés a ConfigurationService előtt
        // különben nem lesznek jól megjelenítve a lokalizált szövegek, színek
        // a lokalizáció a rendszer nyelve alapján állítódik be, ezt kell felülírni a ConfigurationService-el

        // nyelv és színséma lekérdezése, beállítása
        var configurationService = services.GetRequiredService<ConfigurationService>();

        var colorScheme = configurationService.ReadSetting(ConfigurationKey.ColorScheme);
        var language = configurationService.ReadSetting(ConfigurationKey.Language);

        var showInformationalMessages = configurationService.ReadSetting(ConfigurationKey.ShowInformationalMessages);

        // új felhasználóknak alapértelmezetten bekapcsoljuk a tájékoztató üzeneteket
        if (string.IsNullOrWhiteSpace(showInformationalMessages))
        {
            configurationService.SaveSetting(ConfigurationKey.ShowInformationalMessages, true.ToString());
        }

        var cultureInfo = language switch
        {
            "hungarian" => CultureInfo.GetCultureInfo("hu-HU"),
            _ => CultureInfo.InvariantCulture
        };

        RequestedThemeVariant = colorScheme switch
        {
            "dark" => ThemeVariant.Dark,
            "light" => ThemeVariant.Light,
            _ => ThemeVariant.Default
        };

        LocalizationService.ChangeLanguage(cultureInfo);

        var appService = services.GetRequiredService<IApplicationService>();
        _ollamaService = services.GetRequiredService<OllamaService>();
        _dialogService = services.GetRequiredService<DialogService>();
        _databaseInitService = services.GetRequiredService<DatabaseInitService>();
        SharedDbConnection = Task.Run(() => _databaseInitService.GetOpenConnectionAsync()).GetAwaiter().GetResult();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            //feliratkozunk az OnStartup-ra és az OnExitre
            desktop.Startup += OnStartup;
            desktop.Exit += OnExit;
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;

            // Avoid duplicate validations from both Avalonia and the CommunityToolkit.
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();
            appService.InitializeMainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnStartup(object? sender, ControlledApplicationLifetimeStartupEventArgs e)
    {
        // külön szálon, aszinkron fut le elvileg
        // nem valószínű hogy elkapna valaha is bármilyen kivételt de biztos ami biztos
        Task.Run(async () =>
        {
            try
            {
                await _ollamaService!.Start();
            }
            catch (Exception ex)
            {
                // TODO: ehelyett majd logolás
                Console.WriteLine(ex);
            }
        });
    }

    private void OnExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        _ollamaService?.Stop();
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }

    private void About_OnClick(object? sender, EventArgs e)
    {
        // ezt később majd vmi fancy dialogra
        _dialogService?.ShowInfoDialog(
            "Avallama - " + LocalizationService.GetString("VERSION")
                          + "\n\nCopyright (c) " + LocalizationService.GetString("DEVELOPER_NAMES")
                          + "\n\n" + LocalizationService.GetString("LICENSE")
                          + "\n\n" + LocalizationService.GetString("FROM_ORG") + " (github.com/4foureyes/avallama)"
        );
    }
}
