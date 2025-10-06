// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

using avallama.Utilities;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.TextFormatting;
using Xunit;
using Colors = Avalonia.Media.Colors;

namespace avallama.Tests;

public class TextHelperTests
{
    private const string MainText = "Lorem ipsum dolor sit amet, consectetur adipiscing elit.";
    private const string SubText = "Generation speed: 30 tokens/sec";

    private readonly Thickness _padding = new(12);

    private Point GetMainTextPosition()
    {
        var scale = LayoutHelper.GetLayoutScale(new Layoutable());
        var roundedPadding = LayoutHelper.RoundLayoutThickness(_padding, scale, scale);

        var mainTextPosX = roundedPadding.Left;
        var mainTextPosY = roundedPadding.Top;
        return new Point(mainTextPosX, mainTextPosY);
    }

    private TextLayout CreateTextLayout()
    {
        var typeface = new Typeface(
            FontFamily.Default
        );

        return new TextLayout(
            MainText,
            typeface,
            null,
            12,
            new ImmutableSolidColorBrush(Colors.Black),
            TextAlignment.Left,
            TextWrapping.Wrap
        );
    }

    [AvaloniaFact]
    public void IsPointerOverText_WhenPointerIsOverText_ReturnsTrue()
    {
        var mainTextLayout = CreateTextLayout();

        var mainTextLayoutPosition = GetMainTextPosition();

        var pointerPosition = new Point(
            x: mainTextLayoutPosition.X + mainTextLayout.Width / 2,
            y: mainTextLayoutPosition.Y + mainTextLayout.Height / 2
        );

        var result = TextHelper.IsPointerOverText(mainTextLayout, mainTextLayoutPosition, pointerPosition);
        Assert.True(result);
    }

    [AvaloniaFact]
    public void IsPointerOverText_WhenPointerIsNotOverText_ReturnsFalse()
    {
        var mainTextLayout = CreateTextLayout();

        var mainTextLayoutPosition = GetMainTextPosition();

        var pointerPosition = new Point(
            x: mainTextLayoutPosition.X,
            y: mainTextLayoutPosition.Y
        );

        var result = TextHelper.IsPointerOverText(mainTextLayout, mainTextLayoutPosition, pointerPosition);
        Assert.False(result);
    }

    [AvaloniaFact]
    public void TextIndexFromPointer_WhenPointerIsOverCharacter_ReturnsIndexOfCharacter()
    {
        var mainTextLayout = CreateTextLayout();

        var mainTextLayoutPosition = GetMainTextPosition();

        var characterWidth = mainTextLayout.Width / mainTextLayout.TextLines[0].Length;

        // 7th character
        var pointerPosition = new Point(
            x: mainTextLayoutPosition.X + characterWidth * 6,
            y: mainTextLayoutPosition.Y
        );

        var index = TextHelper.GetTextIndexFromPointer(
            mainTextLayout,
            MainText,
            _padding,
            pointerPosition
        );

        Assert.Equal(6, index);
        Assert.Equal('i', MainText[index]);

        // 11th character
        pointerPosition = new Point(
            x: mainTextLayoutPosition.X + characterWidth * 10,
            y: mainTextLayoutPosition.Y
        );

        index = TextHelper.GetTextIndexFromPointer(
            mainTextLayout,
            MainText,
            _padding,
            pointerPosition
        );

        Assert.Equal('m', MainText[index]);
        Assert.Equal(10, index);

        // First character 'L'
        pointerPosition = new Point(
            x: mainTextLayoutPosition.X,
            y: mainTextLayoutPosition.Y
        );

        index = TextHelper.GetTextIndexFromPointer(
            mainTextLayout,
            MainText,
            _padding,
            pointerPosition
        );

        Assert.Equal('L', MainText[index]);
        Assert.Equal(0, index);

        // Last character '.'
        pointerPosition = new Point(
            x: mainTextLayoutPosition.X + characterWidth * (MainText.Length - 1),
            y: mainTextLayoutPosition.Y
        );

        index = TextHelper.GetTextIndexFromPointer(
            mainTextLayout,
            MainText,
            _padding,
            pointerPosition
        );

        Assert.Equal('.', MainText[index]);
        Assert.Equal(MainText.Length - 1, index);
    }

    [AvaloniaFact]
    public void TextIndexFromPointer_WhenPointerIsOutOfBounds_ReturnsClampedIndex()
    {
        var mainTextLayout = CreateTextLayout();

        var mainTextLayoutPosition = GetMainTextPosition();

        var pointerPosition = new Point(
            x: mainTextLayoutPosition.X + 1000,
            y: mainTextLayoutPosition.Y + 1000
        );

        var index = TextHelper.GetTextIndexFromPointer(
            mainTextLayout,
            MainText,
            _padding,
            pointerPosition
        );

        Assert.Equal(MainText.Length, index);

        pointerPosition = new Point(
            x: -1000,
            y: -1000
        );

        index = TextHelper.GetTextIndexFromPointer(
            mainTextLayout,
            MainText,
            _padding,
            pointerPosition
        );

        Assert.Equal(0, index);
    }
}
