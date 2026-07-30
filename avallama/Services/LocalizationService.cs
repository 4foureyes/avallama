// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

using System;
using System.Globalization;
using Avalonia.Markup.Xaml;

namespace avallama.Services;
public class LocalizationService : MarkupExtension
{
    private static readonly System.Threading.Lock Lock = new();
    private static CultureInfo _resourceCulture = CultureInfo.CurrentUICulture;

    // Requesting a resource manager instance
    private static System.Resources.ResourceManager ResourceManager {
        get {
            // thread-safe initialization
            if (field != null) return field;
            lock (Lock)
            {
                field ??= new System.Resources.ResourceManager("avallama.Assets.Localization.Resources",
                    typeof(LocalizationService).Assembly);
            }
            return field;
        }
    }

    public static void ChangeLanguage(CultureInfo cultureInfo)
    {
        _resourceCulture = cultureInfo;
    }

    // Requesting localized text by key
    public static string GetString(string key)
    {
        var localizedString = ResourceManager.GetString(key, _resourceCulture) ?? $"[{key}]";
        localizedString = localizedString.Replace("\\n", Environment.NewLine);
        return localizedString;
    }

    // MarkupExtension
    public string Key { get; set; } = string.Empty;
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return GetString(Key);
    }
}
