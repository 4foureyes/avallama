// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.TextFormatting;

namespace avallama.Controls
{
    /// <summary>
    /// Represents a text selection model capable of rendering highlighted selections,
    /// updating selection ranges, and extracting selected text from a source string.
    /// </summary>
    /// <remarks>
    /// This class provides lightweight selection management for text rendered
    /// via Avalonia’s <see cref="TextLayout"/>. It does not depend on Avalonia’s
    /// built-in text editing framework, making it ideal for custom controls like
    /// <c>MessageItem</c> or <c>ConversationItem</c>.
    /// </remarks>
    public class TextSelection
    {
        /// <summary>
        /// Gets or sets the starting index of the current text selection.
        /// </summary>
        public int Start { get; set; }

        /// <summary>
        /// Gets or sets the ending index of the current text selection.
        /// </summary>
        public int End { get; set; }

        /// <summary>
        /// Gets the currently selected substring from the source text.
        /// </summary>
        public string SelectedText { get; private set; } = string.Empty;

        /// <summary>
        /// Gets or sets the brush used to render the selection highlight.
        /// </summary>
        public IBrush? SelectionBrush { get; set; }

        /// <summary>
        /// Renders the current selection highlight onto the provided <see cref="DrawingContext"/>.
        /// </summary>
        /// <param name="context">The drawing context to draw onto.</param>
        /// <param name="textLayout">The layout associated with the rendered text.</param>
        /// <param name="padding">The padding to offset the selection rendering by.</param>
        /// <remarks>
        /// This method draws semi-transparent rectangles corresponding to the selected
        /// text range. It will return early if no selection is active.
        /// </remarks>
        public void Render(DrawingContext context, TextLayout? textLayout, Thickness padding)
        {
            if (Start == End || textLayout is null)
                return;

            // Calculate the normalized selection range (to handle reversed dragging).
            var selectionFrom = Math.Min(Start, End);
            var selectionRange = Math.Max(Start, End) - selectionFrom;

            // Compute rectangles representing the selected glyphs.
            var rects = textLayout.HitTestTextRange(selectionFrom, selectionRange);

            // Use provided brush color or fallback to a teal highlight.
            var selectedColor = (SelectionBrush as ImmutableSolidColorBrush)?.Color ?? Colors.Teal;
            var selectionBrush = new ImmutableSolidColorBrush(selectedColor, 0.5);

            // Apply translation for control padding.
            var origin = new Point(padding.Left, padding.Top);
            using (context.PushTransform(Matrix.CreateTranslation(origin)))
            {
                foreach (var rect in rects)
                {
                    context.FillRectangle(selectionBrush, PixelRect.FromRect(rect, 1).ToRect(1));
                }
            }
        }

        /// <summary>
        /// Selects an entire word surrounding the given character index.
        /// </summary>
        /// <param name="text">The source text.</param>
        /// <param name="index">The index used to locate the word.</param>
        /// <remarks>
        /// This method expands the selection both left and right until whitespace or
        /// non-alphanumeric characters are encountered. If the character at the given
        /// index is not part of a word, the method returns early.
        /// </remarks>
        public void SelectWordByIndex(string? text, int index)
        {
            if (text == null || index < 0 || index >= text.Length)
                return;

            if (char.IsWhiteSpace(text[index]) || !char.IsLetterOrDigit(text[index]))
                return;

            int wordStartIndex = index;
            int wordEndIndex = index;

            // Scan left to find the start of the word.
            for (int i = index; i >= 0 && !char.IsWhiteSpace(text[i]) && char.IsLetterOrDigit(text[i]); i--)
            {
                wordStartIndex = i;
            }

            // Scan right to find the end of the word.
            for (int i = index; i < text.Length && !char.IsWhiteSpace(text[i]) && char.IsLetterOrDigit(text[i]); i++)
            {
                wordEndIndex = i;
            }

            Start = wordStartIndex;
            End = wordEndIndex + 1;
            Update(text);
        }

        /// <summary>
        /// Selects an entire paragraph that contains the specified index.
        /// </summary>
        /// <param name="text">The source text.</param>
        /// <param name="index">The index used to determine which paragraph to select.</param>
        /// <remarks>
        /// A paragraph is defined as text separated by newline (<c>"\n"</c>) characters.
        /// If no separators are found, the entire text will be selected.
        /// </remarks>
        public void SelectParagraphByIndex(string? text, int index)
        {
            if (text == null)
                return;

            const string separator = "\n";

            int paragraphStartIndex;
            int paragraphEndIndex;

            // Find the start of the paragraph.
            var firstSeparatorPosition = text.LastIndexOf(separator, index, StringComparison.Ordinal);
            paragraphStartIndex = firstSeparatorPosition == -1
                ? 0
                : firstSeparatorPosition + separator.Length;

            // Find the end of the paragraph.
            var lastSeparatorPosition = text.IndexOf(separator, index, StringComparison.Ordinal);
            paragraphEndIndex = lastSeparatorPosition == -1
                ? text.Length - 1
                : lastSeparatorPosition;

            Start = paragraphStartIndex;
            End = paragraphEndIndex + 1;
            Update(text);
        }

        /// <summary>
        /// Selects the entire content of the provided text.
        /// </summary>
        /// <param name="text">The source text to select.</param>
        public void SelectAll(string? text)
        {
            if (text == null)
                return;

            Start = 0;
            End = text.Length;
            Update(text);
        }

        /// <summary>
        /// Updates the <see cref="SelectedText"/> property based on the current selection range.
        /// </summary>
        /// <param name="text">The text source from which the selection is taken.</param>
        public void Update(string? text)
        {
            if (text == null)
                return;

            var selectionFrom = Math.Min(Start, End);
            var selectionRange = Math.Max(Start, End) - selectionFrom;
            SelectedText = text.AsSpan(selectionFrom, selectionRange).ToString();
        }

        /// <summary>
        /// Clears the current selection and resets the selected text.
        /// </summary>
        public void Clear()
        {
            End = Start;
            SelectedText = string.Empty;
        }
    }
}
