// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

using System;
using avallama.Utilities;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace avallama.Controls;

/// <summary>
/// Szövegdoboz állítható háttérrel, két szöveggel, személyre szabott propertykkel
/// </summary>
public class TextItem : Control
{
    // AXAML Styled Propertyk
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<TextItem, string?>(nameof(Text));

    public static readonly StyledProperty<IBrush?> TextColorProperty =
        AvaloniaProperty.Register<TextItem, IBrush?>(nameof(TextColor));

    public static readonly StyledProperty<string?> SubTextProperty =
        AvaloniaProperty.Register<TextItem, string?>(nameof(SubText));

    public static readonly StyledProperty<IBrush?> SubTextColorProperty =
        AvaloniaProperty.Register<TextItem, IBrush?>(nameof(SubTextColor));

    public static readonly StyledProperty<Thickness?> PaddingProperty =
        AvaloniaProperty.Register<TextItem, Thickness?>(nameof(Padding));

    public static readonly StyledProperty<CornerRadius?> CornerRadiusProperty =
        AvaloniaProperty.Register<TextItem, CornerRadius?>(nameof(CornerRadius));

    public static readonly StyledProperty<double?> TextFontSizeProperty =
        AvaloniaProperty.Register<TextItem, double?>(nameof(TextFontSize));

    public static readonly StyledProperty<double?> SubTextFontSizeProperty =
        AvaloniaProperty.Register<TextItem, double?>(nameof(SubTextFontSize));

    public static readonly StyledProperty<TextAlignment?> TextAlignmentProperty =
        AvaloniaProperty.Register<TextItem, TextAlignment?>(nameof(TextAlignment));

    public static readonly StyledProperty<TextAlignment?> SubTextAlignmentProperty =
        AvaloniaProperty.Register<TextItem, TextAlignment?>(nameof(SubTextAlignment));

    public static readonly StyledProperty<FontFamily?> FontFamilyProperty =
        AvaloniaProperty.Register<TextItem, FontFamily?>(nameof(FontFamily));

    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        AvaloniaProperty.Register<TextItem, IBrush?>(nameof(Background));

    public static readonly StyledProperty<double?> SpacingProperty =
        AvaloniaProperty.Register<TextItem, double?>(nameof(Spacing));

    public static readonly StyledProperty<double?> LineHeightProperty =
        AvaloniaProperty.Register<TextItem, double?>(nameof(LineHeight));

    public static readonly StyledProperty<int?> MaxLinesProperty =
        AvaloniaProperty.Register<TextItem, int?>(nameof(MaxLines));

    public static readonly StyledProperty<TextTrimming?> TextTrimmingProperty =
        AvaloniaProperty.Register<TextItem, TextTrimming?>(nameof(TextTrimming));

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public IBrush? TextColor
    {
        get => GetValue(TextColorProperty);
        set => SetValue(TextColorProperty, value);
    }

    public string? SubText
    {
        get => GetValue(SubTextProperty);
        set => SetValue(SubTextProperty, value);
    }

    public IBrush? SubTextColor
    {
        get => GetValue(SubTextColorProperty);
        set => SetValue(SubTextColorProperty, value);
    }

    public Thickness? Padding
    {
        get => GetValue(PaddingProperty);
        set => SetValue(PaddingProperty, value);
    }

    public CornerRadius? CornerRadius
    {
        get => GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public double? TextFontSize
    {
        get => GetValue(TextFontSizeProperty);
        set => SetValue(TextFontSizeProperty, value);
    }

    public double? SubTextFontSize
    {
        get => GetValue(SubTextFontSizeProperty);
        set => SetValue(SubTextFontSizeProperty, value);
    }

    public TextAlignment? TextAlignment
    {
        get => GetValue(TextAlignmentProperty);
        set => SetValue(TextAlignmentProperty, value);
    }

    public TextAlignment? SubTextAlignment
    {
        get => GetValue(SubTextAlignmentProperty);
        set => SetValue(SubTextAlignmentProperty, value);
    }

    public FontFamily? FontFamily
    {
        get => GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public IBrush? Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    public double? Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    public double? LineHeight
    {
        get => GetValue(LineHeightProperty);
        set => SetValue(LineHeightProperty, value);
    }

    public int? MaxLines
    {
        get => GetValue(MaxLinesProperty);
        set => SetValue(MaxLinesProperty, value);
    }

    public TextTrimming? TextTrimming
    {
        get => GetValue(TextTrimmingProperty);
        set => SetValue(TextTrimmingProperty, value);
    }

    protected TextLayout? MainTextLayout;
    protected TextLayout? SubTextLayout;

    protected Point? MainTextLayoutPosition;

    // a MaxWidth és MaxHeight megfelelő beállításához kell a Create(Sub)TextLayoutnak
    protected Size Constraint = Size.Infinity;

    public override void Render(DrawingContext context)
    {
        // Háttér renderelése
        RenderBackground(context);

        // Szövegek renderelése
        RenderText(context);
    }

    protected virtual void RenderBackground(DrawingContext context)
    {
        var cornerRadius = CornerRadius ?? new CornerRadius(0, 0, 0, 0);
        context.DrawRectangle(Background, null,
            new RoundedRect(
                new Rect(Bounds.Size),
                cornerRadius.TopLeft,
                cornerRadius.TopRight,
                cornerRadius.BottomRight,
                cornerRadius.BottomLeft
            )
        );
    }

    // Létrehozott TextLayoutok renderelése (amennyiben nem null)
    protected void RenderText(DrawingContext context)
    {
        var mainTextPosition = TextHelper.CalculateMainTextPosition(
            this,
            MainTextLayout,
            SubTextLayout,
            TextAlignment,
            Bounds.Width,
            Padding
        );

        MainTextLayoutPosition = mainTextPosition;

        var subTextPosition = TextHelper.CalculateSubTextPosition(
            this,
            MainTextLayout,
            SubTextLayout,
            SubTextAlignment,
            Bounds.Width,
            Padding,
            Spacing
        );

        MainTextLayout?.Draw(context, mainTextPosition);
        SubTextLayout?.Draw(context, subTextPosition);
    }

    // Létrehozza az alap szöveget (amennyiben meg van adva)
    protected virtual TextLayout? CreateTextLayout()
    {
        if (string.IsNullOrEmpty(Text)) return null;

        // typeface beállítása
        var typeface = new Typeface(
            FontFamily ?? FontFamily.Default
        );

        // real-time generálásnál képes több ezres nagyságban létrehozni és felszabadítani TextLayout elemeket
        return new TextLayout(
            Text,
            typeface,
            null,
            TextFontSize ?? 12,
            TextColor,
            TextAlignment ?? Avalonia.Media.TextAlignment.Left,
            TextWrapping.Wrap,
            textTrimming: TextTrimming,
            null,
            FlowDirection.LeftToRight,
            Constraint.Width,
            Constraint.Height,
            LineHeight ?? double.NaN,
            maxLines: MaxLines ?? 0
        );
    }

    // Létrehozza az alsó szöveget (amennyiben meg van adva)
    protected virtual TextLayout? CreateSubTextLayout()
    {
        if (!string.IsNullOrEmpty(SubText))
        {

            return new TextLayout(
                SubText,
                new Typeface(FontFamily ?? FontFamily.Default),
                SubTextFontSize ?? 8,
                SubTextColor,
                SubTextAlignment ?? Avalonia.Media.TextAlignment.Right,
                TextWrapping.Wrap,
                null,
                null,
                FlowDirection.LeftToRight,
                Constraint.Width,
                Constraint.Height
            );
        }

        return null;
    }

    protected void InvalidateTextLayouts()
    {
        MainTextLayout?.Dispose();
        MainTextLayout = null;
        SubTextLayout?.Dispose();
        SubTextLayout = null;
    }

    protected void CreateTextLayouts()
    {
        MainTextLayout = CreateTextLayout();
        SubTextLayout = CreateSubTextLayout();
    }

    protected override void OnMeasureInvalidated()
    {
        // felszabadítja a textLayoutokat
        InvalidateTextLayouts();
        base.OnMeasureInvalidated();
    }

    // Felméri hogy mennyi helyre van szüksége a Controlnak
    protected override Size MeasureOverride(Size availableSize)
    {
        var scale = LayoutHelper.GetLayoutScale(this);

        // LayoutHelperrel roundolja a Thicknesst (megadott Paddingot) magas dpi képernyőkre, a megfelelő koordinátákhoz
        var padding = LayoutHelper.RoundLayoutThickness(Padding ?? new Thickness(0, 0, 0, 0), scale, scale);
        var deflatedSize = availableSize.Deflate(padding); // kiveszi az elérhető helyből a paddingot

        // ha a constraint nem egyezik akkor reseteli a textLayoutokat és újraigazítja őket
        if (Constraint != deflatedSize)
        {
            MainTextLayout?.Dispose();
            MainTextLayout = null;
            SubTextLayout?.Dispose();
            SubTextLayout = null;
            Constraint = deflatedSize;

            MainTextLayout = CreateTextLayout();
            SubTextLayout = CreateSubTextLayout();
        }

        // a lehető legnagyobb szélesség a textlayoutokra nézve
        var textLayoutWidth = MainTextLayout == null
            ? 0
            : MainTextLayout.OverhangLeading + MainTextLayout.WidthIncludingTrailingWhitespace + MainTextLayout.OverhangTrailing;
        var subTextLayoutWidth = SubTextLayout == null
            ? 0
            : SubTextLayout.OverhangLeading + SubTextLayout.WidthIncludingTrailingWhitespace +
              SubTextLayout.OverhangTrailing;

        // a lehető legnagyobb hosszúság a textlayoutokra nézve
        var textLayoutHeight = MainTextLayout?.Height ?? 0;
        var subTextLayoutHeight = SubTextLayout?.Height ?? 0;

        double spacing;
        // ha valamelyik textlayout hiányzik a spacingot 0-ra állítjuk
        if (textLayoutHeight == 0 || subTextLayoutHeight == 0 || Spacing == null)
        {
            spacing = 0.0;
        }
        else
        {
            spacing = Spacing.Value;
        }

        // max szélesség a kettő között
        var width = Math.Max(textLayoutWidth, subTextLayoutWidth);

        // végső méret a szélességgel és a max magassággal inflatelve a paddinggel
        var size = LayoutHelper.RoundLayoutSizeUp(
            new Size(width, textLayoutHeight + subTextLayoutHeight + spacing).Inflate(padding), 1, 1);

        return size;
    }

    // pozicionálja az elemeket a számukra elérhető hely alapján több metódussal együtt dolgozva (Arrange, ArrangeCore)
    // több infó: https://docs.avaloniaui.net/docs/basics/user-interface/building-layouts/#measuring-and-arranging-children
    protected override Size ArrangeOverride(Size finalSize)
    {
        var scale = LayoutHelper.GetLayoutScale(this);
        var padding = LayoutHelper.RoundLayoutThickness(Padding ?? new Thickness(0, 0, 0, 0), scale, scale);
        var availableSize = finalSize.Deflate(padding);

        if (Constraint != availableSize)
        {
            MainTextLayout?.Dispose();
            MainTextLayout = null;
            SubTextLayout?.Dispose();
            SubTextLayout = null;
            Constraint = availableSize;

            MainTextLayout = CreateTextLayout();
            SubTextLayout = CreateSubTextLayout();
        }

        return finalSize;
    }

    // ha bármelyik property megváltozik akkor invalidáljuk a jelenlegi textlayoutokat és újat hozunk létre
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        switch (change.Property.Name)
        {
            // Méretet érintő változások:
            case nameof(Text):
            case nameof(SubText):
            case nameof(TextFontSize):
            case nameof(SubTextFontSize):
            case nameof(FontFamily):
            case nameof(Spacing):
            case nameof(Padding):
            case nameof(LineHeight):
            case nameof(MaxLines):
            case nameof(TextTrimming):
            {
                InvalidateMeasure();
                break;
            }

            // Vizuális változások:
            case nameof(TextColor):
            case nameof(SubTextColor):
            case nameof(Background):
            case nameof(CornerRadius):
            {
                InvalidateTextLayouts();
                CreateTextLayouts();
                InvalidateVisual();
                break;
            }

            case nameof(TextAlignment):
            case nameof(SubTextAlignment):
            {
                InvalidateArrange();
                break;
            }
        }
    }
}
