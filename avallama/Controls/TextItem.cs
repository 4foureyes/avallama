// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace avallama.Controls
{
    /// <summary>
    /// A customizable text box control with configurable background, two text lines (main and sub),
    /// and several styling properties such as font, padding, corner radius, and spacing.
    /// </summary>
    public class TextItem : Control
    {
        #region Styled Properties

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

        #endregion

        #region Properties

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

        #endregion

        #region Protected Fields

        protected TextLayout? MainTextLayout;
        protected TextLayout? SubTextLayout;
        protected Point? MainTextLayoutPosition;

        /// <summary>
        /// Constraint used for measuring text layouts to ensure correct width/height calculations.
        /// </summary>
        protected Size Constraint = Size.Infinity;

        #endregion

        #region Rendering

        /// <summary>
        /// Renders the background and text contents of the control.
        /// </summary>
        /// <param name="context">The drawing context used for rendering.</param>
        public override void Render(DrawingContext context)
        {
            RenderBackground(context);
            RenderText(context);
        }

        /// <summary>
        /// Renders the control's background using the configured brush and corner radius.
        /// </summary>
        /// <param name="context">The drawing context used for rendering.</param>
        protected virtual void RenderBackground(DrawingContext context)
        {
            var cornerRadius = CornerRadius ?? new CornerRadius(0);
            context.DrawRectangle(
                Background,
                null,
                new RoundedRect(
                    new Rect(Bounds.Size),
                    cornerRadius.TopLeft,
                    cornerRadius.TopRight,
                    cornerRadius.BottomRight,
                    cornerRadius.BottomLeft));
        }

        /// <summary>
        /// Draws the main and sub text layouts, if available, at their calculated positions.
        /// </summary>
        /// <param name="context">The drawing context used for rendering.</param>
        protected void RenderText(DrawingContext context)
        {
            var scale = LayoutHelper.GetLayoutScale(this);
            var roundedPadding = LayoutHelper.RoundLayoutThickness(Padding ?? new Thickness(0), scale, scale);

            var mainTextPosition = new Point(roundedPadding.Left, roundedPadding.Top);
            MainTextLayoutPosition = mainTextPosition;

            var subTextY = roundedPadding.Top;

            if (MainTextLayout?.Height > 0)
            {
                subTextY += MainTextLayout.Height + (Spacing ?? 0.0);
            }
            var subTextPosition = new Point(roundedPadding.Left, subTextY);

            MainTextLayout?.Draw(context, mainTextPosition);
            SubTextLayout?.Draw(context, subTextPosition);
        }

        #endregion

        #region Text Layout Creation

        /// <summary>
        /// Creates a new <see cref="TextLayout"/> for the main text if available.
        /// </summary>
        /// <returns>A configured <see cref="TextLayout"/> instance or null if text is empty.</returns>
        protected virtual TextLayout? CreateTextLayout()
        {
            if (string.IsNullOrEmpty(Text))
                return null;

            var typeface = new Typeface(FontFamily ?? FontFamily.Default);

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
                flowDirection: FlowDirection.LeftToRight,
                Constraint.Width,
                Constraint.Height,
                LineHeight ?? double.NaN,
                maxLines: MaxLines ?? 0);
        }

        /// <summary>
        /// Creates a new <see cref="TextLayout"/> for the subtext if available.
        /// </summary>
        /// <returns>A configured <see cref="TextLayout"/> instance or null if subtext is empty.</returns>
        protected virtual TextLayout? CreateSubTextLayout()
        {
            if (string.IsNullOrEmpty(SubText))
                return null;

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
                Constraint.Height);
        }

        /// <summary>
        /// Disposes and clears the existing text layouts.
        /// </summary>
        protected void InvalidateTextLayouts()
        {
            MainTextLayout?.Dispose();
            MainTextLayout = null;
            SubTextLayout?.Dispose();
            SubTextLayout = null;
        }

        /// <summary>
        /// Creates both the main and sub text layouts.
        /// </summary>
        protected void CreateTextLayouts()
        {
            MainTextLayout = CreateTextLayout();
            SubTextLayout = CreateSubTextLayout();
        }

        #endregion

        #region Layout Overrides

        /// <summary>
        /// Called when the measure is invalidated. Disposes existing text layouts and recreates them.
        /// </summary>
        protected override void OnMeasureInvalidated()
        {
            InvalidateTextLayouts();
            base.OnMeasureInvalidated();
        }

        /// <summary>
        /// Measures the required size of the control, considering padding, text size, and spacing.
        /// </summary>
        /// <param name="availableSize">The available size for layout.</param>
        /// <returns>The desired size of the control.</returns>
        protected override Size MeasureOverride(Size availableSize)
        {
            var scale = LayoutHelper.GetLayoutScale(this);
            var padding = LayoutHelper.RoundLayoutThickness(Padding ?? new Thickness(0), scale, scale);
            var deflatedSize = availableSize.Deflate(padding);

            if (Constraint != deflatedSize)
            {
                InvalidateTextLayouts();
                Constraint = deflatedSize;
                CreateTextLayouts();
            }

            var textWidth = MainTextLayout == null
                ? 0
                : MainTextLayout.OverhangLeading +
                  MainTextLayout.WidthIncludingTrailingWhitespace +
                  MainTextLayout.OverhangTrailing;

            var subTextWidth = SubTextLayout == null
                ? 0
                : SubTextLayout.OverhangLeading +
                  SubTextLayout.WidthIncludingTrailingWhitespace +
                  SubTextLayout.OverhangTrailing;

            var textHeight = MainTextLayout?.Height ?? 0;
            var subTextHeight = SubTextLayout?.Height ?? 0;

            var spacing = (textHeight == 0 || subTextHeight == 0 || Spacing == null)
                ? 0.0
                : Spacing.Value;

            var width = Math.Max(textWidth, subTextWidth);
            var height = textHeight + subTextHeight + spacing;

            var finalSize = LayoutHelper.RoundLayoutSizeUp(
                new Size(width, height).Inflate(padding), 1, 1);

            return finalSize;
        }

        /// <summary>
        /// Positions and arranges the control’s contents within its final layout bounds.
        /// </summary>
        /// <param name="finalSize">The final size allocated to the control.</param>
        /// <returns>The final arranged size.</returns>
        protected override Size ArrangeOverride(Size finalSize)
        {
            var scale = LayoutHelper.GetLayoutScale(this);
            var padding = LayoutHelper.RoundLayoutThickness(Padding ?? new Thickness(0), scale, scale);
            var availableSize = finalSize.Deflate(padding);

            if (Constraint != availableSize)
            {
                InvalidateTextLayouts();
                Constraint = availableSize;
                CreateTextLayouts();
            }

            return finalSize;
        }

        #endregion

        #region Property Change Handling

        /// <summary>
        /// Handles property change events to update layout, rendering, or visuals accordingly.
        /// </summary>
        /// <param name="change">The property change event arguments.</param>
        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            switch (change.Property.Name)
            {
                // Properties that affect size and layout
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
                    InvalidateMeasure();
                    break;

                // Visual properties
                case nameof(TextColor):
                case nameof(SubTextColor):
                case nameof(Background):
                case nameof(CornerRadius):
                    InvalidateTextLayouts();
                    CreateTextLayouts();
                    InvalidateVisual();
                    break;

                // Alignment properties
                case nameof(TextAlignment):
                case nameof(SubTextAlignment):
                    InvalidateArrange();
                    break;
            }
        }

        #endregion
    }
}
