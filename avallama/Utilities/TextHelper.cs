// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

using System;
using Avalonia;
using Avalonia.Media.TextFormatting;

namespace avallama.Utilities
{
    /// <summary>
    /// Provides helper methods for working with <see cref="TextLayout"/> objects,
    /// including hit testing, pointer detection, and text index calculation.
    /// </summary>
    public static class TextHelper
    {
        /// <summary>
        /// Determines whether a given pointer position is currently over any visible text area
        /// within a <see cref="TextLayout"/>.
        /// </summary>
        /// <param name="textLayout">The text layout to evaluate.</param>
        /// <param name="textLayoutPosition">The position of the layout within its parent coordinate space.</param>
        /// <param name="pointerPosition">The current pointer position.</param>
        /// <returns>
        /// <see langword="true"/> if the pointer is located within the rendered bounds of the text;
        /// otherwise, <see langword="false"/>.
        /// </returns>
        public static bool IsPointerOverText(TextLayout? textLayout, Point? textLayoutPosition, Point pointerPosition)
        {
            if (textLayout == null || textLayoutPosition == null)
                return false;

            // Calculate the layout's bounding box.
            var textFromX = textLayoutPosition.Value.X;
            var textToX = textLayoutPosition.Value.X + textLayout.Width;

            var textFromY = textLayoutPosition.Value.Y;
            var textToY = textLayoutPosition.Value.Y + textLayout.Height;

            // Quick reject: pointer is outside layout bounds.
            if (pointerPosition.X < textFromX || pointerPosition.X > textToX ||
                pointerPosition.Y < textFromY || pointerPosition.Y > textToY)
            {
                return false;
            }

            // Pointer coordinates relative to the text layout’s top-left corner.
            var pointerPosXInBox = Math.Round(pointerPosition.X - textFromX, 2);
            var pointerPosYInBox = Math.Round(pointerPosition.Y - textFromY, 2);

            // Approximate line height for calculating which text line the pointer is over.
            var textLineHeight = Math.Round(textLayout.Height / textLayout.TextLines.Count, 2);

            // Determine the index of the text line the pointer is over.
            var linePointerPosY = Math.Round(pointerPosYInBox / textLineHeight, 2);
            var linePointerIndex = Math.Clamp((int)linePointerPosY, 0, textLayout.TextLines.Count - 1);

            // Compute the vertical pixel offset between line height and actual text extent.
            var heightDifference = (textLineHeight - textLayout.TextLines[linePointerIndex].Extent) / 2;

            // Compute vertical start and end positions of the current line.
            var lineStartingPosY = textLineHeight * linePointerIndex;
            var lineEndingPosY = textLineHeight * (linePointerIndex + 1);

            // Calculate the visible text extent’s vertical region (top and bottom).
            var extentStartingPosY = lineStartingPosY + heightDifference;
            var extentEndingPosY = lineEndingPosY - heightDifference;

            // Horizontal and vertical inclusion checks.
            // The pointer is considered "over text" if it falls within both the horizontal width
            // and vertical extent of the line’s glyph area.
            return pointerPosXInBox <= textLayout.TextLines[linePointerIndex].Width &&
                   pointerPosYInBox >= extentStartingPosY &&
                   pointerPosYInBox <= extentEndingPosY;
        }

        /// <summary>
        /// Calculates the text index position based on the pointer’s position within a layout.
        /// Useful for implementing text selection and hit testing, similar to Avalonia’s <c>SelectableTextBlock</c>.
        /// </summary>
        /// <param name="textLayout">The <see cref="TextLayout"/> used for hit testing.</param>
        /// <param name="text">The text content of the layout.</param>
        /// <param name="padding">The padding applied to the layout, which must be considered in hit testing.</param>
        /// <param name="pointerPosition">The pointer position in control coordinates.</param>
        /// <returns>
        /// The zero-based text index corresponding to the pointer position,
        /// clamped between <c>0</c> and <c>text.Length</c>.
        /// Returns <c>-1</c> if the layout or text is null.
        /// </returns>
        public static int GetTextIndexFromPointer(TextLayout? textLayout, string? text, Thickness padding,
            Point pointerPosition)
        {
            if (textLayout == null || text == null)
                return -1;

            // Adjust pointer position based on padding.
            var point = pointerPosition - new Point(padding.Left, padding.Top);

            // Clamp coordinates to the layout’s bounds to avoid out-of-range issues.
            point = new Point(
                Math.Clamp(point.X, 0, Math.Max(textLayout.WidthIncludingTrailingWhitespace, 0)),
                Math.Clamp(point.Y, 0, Math.Max(textLayout.Height, 0))
            );

            // Perform a hit test at the adjusted coordinates.
            var hit = textLayout.HitTestPoint(point);

            // Clamp the resulting text index between 0 and text.Length.
            // Note: We use text.Length (not text.Length - 1) to allow selection that starts after the last character.
            return Math.Clamp(hit.TextPosition, 0, text.Length);
        }
    }
}
