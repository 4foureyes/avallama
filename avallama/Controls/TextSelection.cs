// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.TextFormatting;

namespace avallama.Controls;

public class TextSelection
{
    private IBrush _selectionBrush;

    public TextSelection(IBrush selectionBrush)
    {
        _selectionBrush = selectionBrush;
    }

    public int Start { get; set; }
    public int End { get; set; }
    public string SelectedText { get; set; } = string.Empty;

    public void Render(DrawingContext context, TextLayout? textLayout, Thickness padding)
    {
        if (Start == End || textLayout is null) return;

        // mínusz értékek elkerülése miatt min, max
        var selectionFrom = Math.Min(Start, End);
        var selectionRange = Math.Max(Start, End) - selectionFrom;

        var rects = textLayout.HitTestTextRange(selectionFrom, selectionRange);
        var selectedColor = (_selectionBrush as ImmutableSolidColorBrush)?.Color ?? Colors.Teal;
        var selectionBrush = new ImmutableSolidColorBrush(
            selectedColor,
            0.5
        );

        var paddingLeft = padding.Left;
        var paddingTop = padding.Top;
        var origin = new Point(paddingLeft, paddingTop);
        using (context.PushTransform(Matrix.CreateTranslation(origin)))
        {
            foreach (var rect in rects)
            {
                context.FillRectangle(selectionBrush, PixelRect.FromRect(rect, 1).ToRect(1));
            }
        }
    }

    public void SelectWordByIndex(string? text, int index)
    {
        if (text == null || index < 0 || index >= text.Length) return;
        if (char.IsWhiteSpace(text[index]) || !char.IsLetterOrDigit(text[index])) return;
        var wordStartIndex = 0;
        var wordEndIndex = 0;
        var i = index;

        // balra haladva megnézzük hogy hol kezdődik az adott szó
        for (; i != -1 && !char.IsWhiteSpace(text[i]) && char.IsLetterOrDigit(text[i]); i--)
        {
            wordStartIndex = i;
        }

        i = index;

        // jobbra haladva megnézzük hogy hol végződik az adott szó
        for (; i != text.Length && !char.IsWhiteSpace(text[i]) && char.IsLetterOrDigit(text[i]); i++)
        {
            wordEndIndex = i;
        }

        Start = wordStartIndex;
        End = wordEndIndex + 1;
    }

    public void SelectParagraphByIndex(string? text, int index)
    {
        if (text == null) return;
        const string separator = "\n";
        int paragraphStartIndex, paragraphEndIndex;
        var firstSeparatorPosition = text.LastIndexOf(separator, index, StringComparison.Ordinal);
        if (firstSeparatorPosition == -1)
        {
            paragraphStartIndex = 0;
        }
        else
        {
            paragraphStartIndex = firstSeparatorPosition + separator.Length;
        }

        var lastSeparatorPosition = text.IndexOf(separator, index, StringComparison.Ordinal);
        if (lastSeparatorPosition == -1)
        {
            paragraphEndIndex = text.Length - 1;
        }
        else
        {
            paragraphEndIndex = lastSeparatorPosition;
        }

        Start = paragraphStartIndex;
        End = paragraphEndIndex + 1;
    }

    public void SelectAll(string? text)
    {
        if (text == null) return;
        Start = 0;
        End = text.Length;
    }

    public void Update(string? text)
    {
        if (text == null) return;
        var selectionFrom = Math.Min(Start, End);
        var selectionRange = Math.Max(Start, End) - selectionFrom;
        SelectedText = text.Substring(selectionFrom, selectionRange);
    }

    public void Clear()
    {
        End = Start;
        SelectedText = string.Empty;
    }
}
