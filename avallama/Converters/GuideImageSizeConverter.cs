// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace avallama.Converters;

public class GuideImageSizeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double controlHeight)
        {
            return controlHeight / 1.5;
        }

        return 500;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return new BindingNotification(new NotSupportedException("Guide image size value cannot be converted back."));
    }
}
