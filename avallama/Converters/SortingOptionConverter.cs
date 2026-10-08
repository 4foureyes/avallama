// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

using System;
using System.Globalization;
using avallama.Constants;
using avallama.Services;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace avallama.Converters;

// Converts the sorting option enum to localized string, so that these don't have to be used in the background
public class SortingOptionConverter : IValueConverter
{

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is SortingOption sortingOption)
        {
            return sortingOption switch
            {
                SortingOption.Downloaded => LocalizationService.GetString("Models.Sorting.Downloaded"),
                SortingOption.PullCountAscending => LocalizationService.GetString("Models.Sorting.PullCountAscending"),
                SortingOption.PullCountDescending => LocalizationService.GetString("Models.Sorting.PullCountDescending"),
                SortingOption.SizeAscending => LocalizationService.GetString("Models.Sorting.SizeAscending"),
                SortingOption.SizeDescending => LocalizationService.GetString("Models.Sorting.SizeDescending"),
                _ => null
            };
        }
        return null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return new BindingNotification(new NotSupportedException("SortingOption value cannot be converted back."));
    }
}
