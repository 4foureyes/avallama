// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Media;

namespace avallama.Controls;

/// <summary>
/// Can be used to display an SVG. The FillColor and StrokeColor properties can be used to set the SVG colors.
/// </summary>
public class DynamicSvg : Avalonia.Svg.Svg
{
    /* An SVG can be colored properly with this class by:
     * - Modifying the content of the SVG file so that the parts we want to color are in separate fill and stroke attributes.
     * - This means that if it is in the format style="fill:none;stroke:none;", it should be replaced so that instead of style
     * it is fill="none" stroke="none".
     * - If we want to exclude any element from coloring or set it statically, then it can be defined in a style so that
     * the DynamicSvg does not use it.
     */

    // Maybe add 'fill-opacity' property later
    public static readonly StyledProperty<IBrush?> FillColorProperty =
        AvaloniaProperty.Register<DynamicSvg, IBrush?>("FillColor");

    public static readonly StyledProperty<IBrush?> StrokeColorProperty =
        AvaloniaProperty.Register<DynamicSvg, IBrush?>("StrokeColor");

    public DynamicSvg(IServiceProvider provider) : base(provider)
    {
    }

    public IBrush? FillColor
    {
        get => GetValue(FillColorProperty);
        set => SetValue(FillColorProperty, value);
    }

    public IBrush? StrokeColor
    {
        get => GetValue(StrokeColorProperty);
        set => SetValue(StrokeColorProperty, value);
    }

    private static string ConvertColorToHex(IBrush? propertyColor)
    {
        var colorParse = Color.TryParse(propertyColor?.ToString(), out var color);
        if (!colorParse) return "000000"; // black color if parsing fails
        var rgb = color.ToUInt32();
        var result = $"{rgb.ToString("x8", CultureInfo.InvariantCulture)}"; // parsed to hexadecimal
        return result[2..]; // removes the first 2 characters, otherwise the color cannot be set
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == FillColorProperty)
        {
            SetCurrentValue(CssProperty, $"* {{ fill: #{ConvertColorToHex(FillColor)} }}; ");
        }

        if (change.Property == StrokeColorProperty)
        {
            SetCurrentValue(CssProperty, $"* {{ stroke: #{ConvertColorToHex(StrokeColor)} }}; ");
        }
    }
}
