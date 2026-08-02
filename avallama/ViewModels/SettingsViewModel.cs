// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Threading.Tasks;
using avallama.Constants.Application;
using avallama.Constants.Keys;
using avallama.Services;
using avallama.Services.Ollama;
using avallama.Services.Persistence;
using avallama.Utilities.Network;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace avallama.ViewModels;

public partial class SettingsViewModel : PageViewModel
{
    private readonly DialogService _dialogService;
    private readonly ConfigurationService _configurationService;
    private readonly IMessenger _messenger;
    private readonly INetworkManager _networkManager;
    private const string Url = @"https://github.com/4foureyes/avallama/";

    private int _selectedLanguageIndex;
    private int _defaultLanguageIndex;

    [ObservableProperty] private string _lastModelUpdate = string.Empty;

    // OnPropertyChanges methods instead of ObservableProperty, so that we can handle the set
    public bool RestartNeeded
    {
        get;
        set
        {
            field = value;
            if (value)
            {
                /*
                // asynchronously show restart dialog on the UI thread
                Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    var dialogResult = await _dialogService.ShowConfirmationDialogAsync(
                        title: LocalizationService.GetString("RESTART_NEEDED_DIALOG_TITLE"),
                        positiveButtonText: LocalizationService.GetString("RESTART_NOW"),
                        negativeButtonText: LocalizationService.GetString("LATER"),
                        description: LocalizationService.GetString("RESTART_NEEDED_DIALOG_DESC")
                    );

                    if (dialogResult is ConfirmationResult { Confirmation: ConfirmationType.Positive })
                    {
                        // request to restart the application
                        _messenger.Send(new ApplicationMessage.Restart());
                    }
                });
                */

                // TODO: replace this with the confirmation dialog if app restart is fixed
                _dialogService.ShowInfoDialog(LocalizationService.GetString("RESTART_NEEDED_DIALOG_TITLE"));
            }

            OnPropertyChanged();
        }
    }

    public int SelectedLanguageIndex
    {
        get => _selectedLanguageIndex;
        set
        {
            _selectedLanguageIndex = value;
            OnPropertyChanged();
        }
    }

    public int SelectedThemeIndex
    {
        get;
        set
        {
            field = value;
            OnPropertyChanged();
        }
    }

    public int SelectedScrollIndex
    {
        get;
        set
        {
            field = value;
            OnPropertyChanged();
        }
    }

    public bool IsChangesSavedTextVisible
    {
        get;
        set
        {
            field = value;
            OnPropertyChanged();
        }
    }

    public string ApiHost
    {
        get;
        set
        {
            field = value;
            OnPropertyChanged();
        }
    } = "localhost";

    public string ApiPort
    {
        get;
        set
        {
            field = value;
            OnPropertyChanged();
        }
    } = OllamaApiClient.DefaultApiPort.ToString();

    public bool IsInformationalMessagesVisible
    {
        get;
        set
        {
            field = value;
            OnPropertyChanged();
        }
    }

    public bool IsParallelDownloadEnabled
    {
        get;
        set
        {
            field = value;
            OnPropertyChanged();
        }
    }

    public bool IsUpdateCheckEnabled
    {
        get;
        set
        {
            field = value;
            OnPropertyChanged();
        }
    }

    public string AppVersion => $"v{App.Version.ToString(3)}";

    public SettingsViewModel(DialogService dialogService, ConfigurationService configurationService,
        IMessenger messenger, INetworkManager networkManager)
    {
        Page = ApplicationPage.Settings;
        _dialogService = dialogService;
        _configurationService = configurationService;
        _messenger = messenger;
        _networkManager = networkManager;
        IsChangesSavedTextVisible = false;
        LoadSettings();
    }

    private void LoadSettings()
    {
        var language = _configurationService.ReadSetting(ConfigurationKey.Language);
        SelectedLanguageIndex = language switch
        {
            "hungarian" => 0,
            _ => 1
        };

        _defaultLanguageIndex = SelectedLanguageIndex;
        RestartNeeded = false;

        var theme = _configurationService.ReadSetting(ConfigurationKey.ColorScheme);
        SelectedThemeIndex = theme switch
        {
            "light" => 1,
            "dark" => 2,
            _ => 0
        };

        var scrollToBottom = _configurationService.ReadSetting(ConfigurationKey.ScrollToBottom);
        SelectedScrollIndex = scrollToBottom switch
        {
            "auto" => 0,
            "float" => 1,
            "none" => 2,
            _ => 1
        };

        var hostSetting = _configurationService.ReadSetting(ConfigurationKey.ApiHost);
        ApiHost = string.IsNullOrEmpty(hostSetting) ? "localhost" : hostSetting;
        var portSetting = _configurationService.ReadSetting(ConfigurationKey.ApiPort);
        ApiPort = string.IsNullOrEmpty(portSetting) ? OllamaApiClient.DefaultApiPort.ToString() : portSetting;

        var isInformationalMessagesVisible = _configurationService.ReadSetting(ConfigurationKey.IsInformationalMessagesVisible);
        IsInformationalMessagesVisible = isInformationalMessagesVisible == "True";

        var isParallelDownloadEnabled = _configurationService.ReadSetting(ConfigurationKey.IsParallelDownloadEnabled);
        IsParallelDownloadEnabled = isParallelDownloadEnabled == "True";

        var isUpdateCheckEnabled = _configurationService.ReadSetting(ConfigurationKey.IsUpdateCheckEnabled);
        IsUpdateCheckEnabled = isUpdateCheckEnabled == "True";

        var lastModelUpdate = _configurationService.ReadSetting(ConfigurationKey.LastUpdatedCache);
        LastModelUpdate = LocalizationService.GetString("LAST_UPDATED") + ": " + (!lastModelUpdate.Equals(string.Empty) ? lastModelUpdate : LocalizationService.GetString("NEVER"));
    }

    private void SaveSettings()
    {
        var colorScheme = SelectedThemeIndex switch
        {
            1 => "light",
            2 => "dark",
            _ => "system"
        };

        var language = SelectedLanguageIndex switch
        {
            0 => "hungarian",
            1 => "english",
            _ => string.Empty
        };

        var scrollToBottom = SelectedScrollIndex switch
        {
            0 => "auto",
            1 => "float",
            2 => "none",
            _ => "float"
        };

        _configurationService.SaveSetting(ConfigurationKey.ColorScheme, colorScheme);
        _configurationService.SaveSetting(ConfigurationKey.Language, language);
        _configurationService.SaveSetting(ConfigurationKey.ScrollToBottom, scrollToBottom);

        if (!IsValidHost(ApiHost))
        {
            _dialogService.ShowErrorDialog(LocalizationService.GetString("INVALID_HOST_ERR"));
            ApiHost = _configurationService.ReadSetting(ConfigurationKey.ApiHost);
            return;
        }

        if (!IsValidPort(ApiPort))
        {
            _dialogService.ShowErrorDialog(LocalizationService.GetString("INVALID_PORT_ERR"));
            ApiPort = _configurationService.ReadSetting(ConfigurationKey.ApiPort);
            return;
        }

        _configurationService.SaveSetting(ConfigurationKey.ApiHost, ApiHost);
        _configurationService.SaveSetting(ConfigurationKey.ApiPort, ApiPort);
        _configurationService.SaveSetting(ConfigurationKey.IsInformationalMessagesVisible,
            IsInformationalMessagesVisible.ToString());
        _configurationService.SaveSetting(ConfigurationKey.IsParallelDownloadEnabled,
            IsParallelDownloadEnabled.ToString());
        _configurationService.SaveSetting(ConfigurationKey.IsUpdateCheckEnabled,
            IsUpdateCheckEnabled.ToString());

        _messenger.Send(new ApplicationMessage.ReloadSettings());

        RestartNeeded = _defaultLanguageIndex != _selectedLanguageIndex;
        IsChangesSavedTextVisible = true;
    }

    [RelayCommand]
    public async Task OnHyperlinkClicked()
    {
        if (!await _networkManager.IsInternetAvailableAsync())
        {
            _dialogService.ShowErrorDialog(LocalizationService.GetString("NO_INTERNET_CONNECTION"));
            return;
        }
        Process.Start(new ProcessStartInfo
        {
            FileName = Url,
            UseShellExecute = true
        });
    }

    [RelayCommand]
    public void Save()
    {
        SaveSettings();
    }

    [RelayCommand]
    public void ResetSettingsToDefault()
    {
        // default settings

        // get the system language, if it is hungarian set to that, otherwise set to default (english)
        var systemUiCultureName = CultureInfo.CurrentUICulture.Name;
        SelectedLanguageIndex = systemUiCultureName == "hu-HU" ? 0 : 1;
        SelectedThemeIndex = 0; // system theme
        SelectedScrollIndex = 1; // floating button scroll setting
        ApiHost = OllamaApiClient.DefaultApiHost;
        ApiPort = OllamaApiClient.DefaultApiPort.ToString();
        IsInformationalMessagesVisible = true; // show informational messages
    }

    private static bool IsValidHost(string host)
    {
        return host == "localhost" || IPAddress.TryParse(host, out _);
    }

    private static bool IsValidPort(string port)
    {
        return int.TryParse(port, out var result) && result is > 0 and < 65536;
    }
}
