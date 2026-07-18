// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

using System;
using System.Globalization;
using avallama.Services;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace avallama.Converters;

public class GenerationSpeedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        double and < 0 => LocalizationService.GetString("GENERATION_CANCELED"),
        double speed and > 0 => $"{speed} {LocalizationService.GetString("TOKEN_SEC")}",
        _ => LocalizationService.GetString("GENERATING_MESSAGE")
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return new BindingNotification(
            new NotSupportedException("Generation speed size value cannot be converted back."));
    }
}
