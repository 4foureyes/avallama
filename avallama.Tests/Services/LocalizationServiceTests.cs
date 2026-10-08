// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

using System.Globalization;
using avallama.Services;
using Xunit;

namespace avallama.Tests.Services;

public class LocalizationServiceTests
{
    private readonly CultureInfo _hungarianCultureInfo = new ("hu-HU");
    private readonly CultureInfo _defaultCultureInfo = CultureInfo.InvariantCulture; // English

    [Fact]
    public void GetString_WithDefinedLocalizationKey_ReturnsCorrectLocalizedValue()
    {
        const string key = "Common.Localization.TestValue";

        LocalizationService.ChangeLanguage(_hungarianCultureInfo);
        var hungarianLocalizedText = LocalizationService.GetString(key);

        LocalizationService.ChangeLanguage(_defaultCultureInfo);
        var defaultLocalizedText = LocalizationService.GetString(key);

        Assert.Equal("Lokalizált értékek tesztelése", hungarianLocalizedText);
        Assert.Equal("Testing localization values", defaultLocalizedText);
    }

    [Fact]
    public void GetString_WithUndefinedLocalizationKey_ReturnsUndefinedValue()
    {
        const string key = "Common.Localization.UndefinedValue";

        LocalizationService.ChangeLanguage(_hungarianCultureInfo);
        var hungarianLocalizedText = LocalizationService.GetString(key);

        LocalizationService.ChangeLanguage(_defaultCultureInfo);
        var defaultLocalizedText = LocalizationService.GetString(key);

        Assert.Equal($"[{key}]", hungarianLocalizedText);
        Assert.Equal($"[{key}]", defaultLocalizedText);
    }

    // TODO: add a test that make sures there are no undefined localization keys (i think it's possible)
}
