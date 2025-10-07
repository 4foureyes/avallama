// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

using System;
using System.Windows.Input;
using avallama.Constants;
using avallama.Utilities;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace avallama.Controls
{
    /// <summary>
    /// Represents a conversation list item with support for hover and active visual states,
    /// as well as selectable and command-triggered behavior.
    /// Inherits text rendering and layout logic from <see cref="TextItem"/>.
    /// </summary>
    public class ConversationItem : TextItem
    {
        #region Avalonia Properties

        /// <summary>
        /// Gets or sets the background brush when the item is hovered.
        /// </summary>
        public static readonly StyledProperty<IBrush?> HoverBackgroundProperty =
            AvaloniaProperty.Register<ConversationItem, IBrush?>(nameof(HoverBackground));

        /// <summary>
        /// Gets or sets the background brush when the item is selected.
        /// </summary>
        public static readonly StyledProperty<IBrush?> ActiveBackgroundProperty =
            AvaloniaProperty.Register<ConversationItem, IBrush?>(nameof(ActiveBackground));

        /// <summary>
        /// Gets or sets the text color used when the item is hovered.
        /// </summary>
        public static readonly StyledProperty<IBrush?> HoverTextColorProperty =
            AvaloniaProperty.Register<ConversationItem, IBrush?>(nameof(HoverTextColor));

        /// <summary>
        /// Gets or sets the text color used when the item is selected.
        /// </summary>
        public static readonly StyledProperty<IBrush?> ActiveTextColorProperty =
            AvaloniaProperty.Register<ConversationItem, IBrush?>(nameof(ActiveTextColor));

        /// <summary>
        /// Gets or sets the subtext color used when the item is hovered.
        /// </summary>
        public static readonly StyledProperty<IBrush?> HoverSubTextColorProperty =
            AvaloniaProperty.Register<ConversationItem, IBrush?>(nameof(HoverSubTextColor));

        /// <summary>
        /// Gets or sets the subtext color used when the item is selected.
        /// </summary>
        public static readonly StyledProperty<IBrush?> ActiveSubTextColorProperty =
            AvaloniaProperty.Register<ConversationItem, IBrush?>(nameof(ActiveSubTextColor));

        /// <summary>
        /// Gets or sets the unique identifier for this conversation item.
        /// </summary>
        public static readonly DirectProperty<ConversationItem, Guid?> IdProperty =
            AvaloniaProperty.RegisterDirect<ConversationItem, Guid?>(
                nameof(Id),
                o => o.Id,
                (o, v) => o.Id = v,
                unsetValue: Guid.Empty
            );

        /// <summary>
        /// Gets or sets the currently selected conversation ID.
        /// Used for comparing with <see cref="Id"/> to determine active state.
        /// </summary>
        public static readonly DirectProperty<ConversationItem, Guid?> SelectedIdProperty =
            AvaloniaProperty.RegisterDirect<ConversationItem, Guid?>(
                nameof(SelectedId),
                o => o.SelectedId,
                (o, v) => o.SelectedId = v,
                unsetValue: Guid.Empty
            );

        /// <summary>
        /// Gets or sets the command executed when the item is clicked.
        /// </summary>
        public static readonly StyledProperty<ICommand?> CommandProperty =
            AvaloniaProperty.Register<ConversationItem, ICommand?>(nameof(Command));

        #endregion

        #region Public Properties

        public IBrush? HoverBackground
        {
            get => GetValue(HoverBackgroundProperty);
            set => SetValue(HoverBackgroundProperty, value);
        }

        public IBrush? ActiveBackground
        {
            get => GetValue(ActiveBackgroundProperty);
            set => SetValue(ActiveBackgroundProperty, value);
        }

        public IBrush? HoverTextColor
        {
            get => GetValue(HoverTextColorProperty);
            set => SetValue(HoverTextColorProperty, value);
        }

        public IBrush? ActiveTextColor
        {
            get => GetValue(ActiveTextColorProperty);
            set => SetValue(ActiveTextColorProperty, value);
        }

        public IBrush? HoverSubTextColor
        {
            get => GetValue(HoverSubTextColorProperty);
            set => SetValue(HoverSubTextColorProperty, value);
        }

        public IBrush? ActiveSubTextColor
        {
            get => GetValue(ActiveSubTextColorProperty);
            set => SetValue(ActiveSubTextColorProperty, value);
        }

        private Guid? _id;

        /// <summary>
        /// Gets or sets the unique identifier for this item.
        /// </summary>
        public Guid? Id
        {
            get => _id;
            set => SetAndRaise(IdProperty, ref _id, value);
        }

        private Guid? _selectedId;

        /// <summary>
        /// Gets or sets the identifier of the currently selected conversation.
        /// </summary>
        public Guid? SelectedId
        {
            get => _selectedId;
            set => SetAndRaise(SelectedIdProperty, ref _selectedId, value);
        }

        public ICommand? Command
        {
            get => GetValue(CommandProperty);
            set => SetValue(CommandProperty, value);
        }

        #endregion

        #region Private Fields

        private bool _isPointerOver;

        #endregion

        #region Rendering

        /// <summary>
        /// Draws the background of the control based on hover and selection state.
        /// </summary>
        protected override void RenderBackground(DrawingContext context)
        {
            var background = Background;

            if (Id != null && SelectedId != null)
            {
                if (Id == SelectedId)
                {
                    background = ActiveBackground ?? ColorProvider.GetColor(AppColor.PrimaryContainer);
                }
                else if (_isPointerOver)
                {
                    background = HoverBackground ?? ColorProvider.GetColor(AppColor.SecondaryContainer);
                }
            }

            if (background == null) return;

            var cornerRadius = CornerRadius ?? new CornerRadius(0);
            context.DrawRectangle(
                background,
                null,
                new RoundedRect(
                    new Rect(Bounds.Size),
                    cornerRadius.TopLeft,
                    cornerRadius.TopRight,
                    cornerRadius.BottomRight,
                    cornerRadius.BottomLeft
                )
            );
        }

        #endregion

        #region Text Layout Creation

        /// <summary>
        /// Creates the main text layout and applies appropriate color based on selection or hover state.
        /// </summary>
        protected override TextLayout? CreateTextLayout()
        {
            if (string.IsNullOrEmpty(Text)) return null;

            var typeface = new Typeface(FontFamily ?? FontFamily.Default);
            var textColor = TextColor;

            if (Id != null && SelectedId != null)
            {
                if (Id == SelectedId)
                {
                    textColor = ActiveTextColor ?? TextColor;
                }
                else if (_isPointerOver)
                {
                    textColor = HoverTextColor ?? TextColor;
                }
            }

            return new TextLayout(
                Text,
                typeface,
                null,
                TextFontSize ?? 12,
                textColor,
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

        /// <summary>
        /// Creates the subtext layout and applies hover or active color based on current state.
        /// </summary>
        protected override TextLayout? CreateSubTextLayout()
        {
            if (string.IsNullOrEmpty(SubText)) return null;

            var subTextColor = SubTextColor;

            if (Id != null && SelectedId != null)
            {
                if (Id == SelectedId)
                {
                    subTextColor = ActiveSubTextColor ?? SubTextColor;
                }
                else if (_isPointerOver)
                {
                    subTextColor = HoverSubTextColor ?? SubTextColor;
                }
            }

            return new TextLayout(
                SubText,
                new Typeface(FontFamily ?? FontFamily.Default),
                SubTextFontSize ?? 8,
                subTextColor,
                SubTextAlignment ?? Avalonia.Media.TextAlignment.Right,
                TextWrapping.Wrap,
                null,
                null,
                FlowDirection.LeftToRight,
                Constraint.Width,
                Constraint.Height
            );
        }

        #endregion

        #region Pointer Events

        /// <summary>
        /// Handles pointer movement events. Updates cursor, hover state, and tooltip visibility.
        /// </summary>
        protected override void OnPointerMoved(PointerEventArgs e)
        {
            Cursor = new Cursor(StandardCursorType.Hand);
            _isPointerOver = true;

            // Display tooltip when text is truncated
            if (MainTextLayout != null && MainTextLayout.TextLines.Count > 0 &&
                MainTextLayout.TextLines[0].HasCollapsed)
            {
                var titleToolTip = new ToolTip { Content = Text };
                ToolTip.SetTip(this, titleToolTip);
            }

            InvalidateTextLayouts();
            CreateTextLayouts();
            InvalidateVisual();
        }

        /// <summary>
        /// Handles pointer exit events by clearing hover state and removing tooltip.
        /// </summary>
        protected override void OnPointerExited(PointerEventArgs e)
        {
            _isPointerOver = false;
            ToolTip.SetTip(this, null);
            InvalidateTextLayouts();
            CreateTextLayouts();
            InvalidateVisual();
        }

        /// <summary>
        /// Executes the associated command when the item is clicked.
        /// </summary>
        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            if (Id is null) return;

            if (Command is { } cmd && cmd.CanExecute(Id))
            {
                cmd.Execute(Id);
            }
        }

        #endregion

        #region Property Change Handling

        /// <summary>
        /// Responds to property changes that affect text layout or visual state.
        /// </summary>
        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            switch (change.Property.Name)
            {
                case nameof(Id):
                case nameof(SelectedId):
                    InvalidateTextLayouts();
                    CreateTextLayouts();
                    InvalidateVisual();
                    break;
            }
        }

        #endregion
    }
}
