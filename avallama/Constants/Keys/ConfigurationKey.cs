// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

namespace avallama.Constants.Keys;

public static class ConfigurationKey
{
    public const string OnboardingCompleted = "onboarding-completed";
    public const string Language = "language";
    public const string ColorScheme = "color-scheme";
    public const string ScrollToBottom = "scroll-to-bottom";
    public const string ApiHost = "api-host";
    public const string ApiPort = "api-port";
    public const string IsInformationalMessagesVisible = "is-informational-messages-visible";
    public const string IsParallelDownloadEnabled = "is-parallel-download-enabled";
    public const string LastUpdatedCache = "last-updated-cache";
    public const string IsUpdateCheckEnabled = "is-update-checking-enabled";

    // Legacy unused keys (so we can remove it from config files in newer versions if users still have them)
    public const string InitialScrapeAskDialogHandled = "initial-scrape-ask-dialog-handled";
    public const string FirstTime = "first-time";
}
