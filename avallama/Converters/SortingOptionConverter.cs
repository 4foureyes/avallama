// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

using System;
using System.Globalization;
using avallama.Constants;
using avallama.Services;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace avallama.Converters;

// rendező enum típust alakítja lokalizált szövegre, hogy ne ezeket kelljen használni a háttérben
public class SortingOptionConverter : IValueConverter
{

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is SortingOption sortingOption)
        {
            return sortingOption switch
            {
                SortingOption.Downloaded => LocalizationService.GetString("SORT_DOWNLOADED"),
                SortingOption.ParametersAscending => LocalizationService.GetString("SORT_PARAMETERS_ASCENDING"),
                SortingOption.ParametersDescending => LocalizationService.GetString("SORT_PARAMETERS_DESCENDING"),
                SortingOption.SizeAscending => LocalizationService.GetString("SORT_SIZE_ASCENDING"),
                SortingOption.SizeDescending => LocalizationService.GetString("SORT_SIZE_DESCENDING"),
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
