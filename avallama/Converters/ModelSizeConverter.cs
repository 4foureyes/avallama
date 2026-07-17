// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

using System;
using System.Globalization;
using avallama.Services;
using avallama.Utilities;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace avallama.Converters;

public class ModelSizeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is long modelSize)
        {
            return string.Format(
                LocalizationService.GetString("MODEL_SIZE"),
                ConversionHelper.BytesToReadableSize(modelSize)
            );
        }

        return string.Format(LocalizationService.GetString("MODEL_SIZE"), "0 GB");
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return new BindingNotification(new NotSupportedException("Model size cannot be converted back."));
    }
}
