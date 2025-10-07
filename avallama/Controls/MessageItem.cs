// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

using System.Threading.Tasks;
using avallama.Utilities;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace avallama.Controls
{
    /// <summary>
    /// A specialized <see cref="TextItem"/> control that supports text selection and clipboard operations.
    /// Provides interactive selection, copying, and pointer-based highlighting similar to text editors.
    /// </summary>
    public class MessageItem : TextItem
    {
        #region Styled Properties

        /// <summary>
        /// Defines the <see cref="SelectionBrush"/> property, which determines the brush used
        /// to highlight selected text within the control.
        /// </summary>
        public static readonly StyledProperty<IBrush> SelectionBrushProperty =
            AvaloniaProperty.Register<MessageItem, IBrush>(nameof(SelectionBrush));

        /// <summary>
        /// Gets or sets the brush used to render the background of the selected text.
        /// </summary>
        public IBrush SelectionBrush
        {
            get => GetValue(SelectionBrushProperty);
            set => SetValue(SelectionBrushProperty, value);
        }

        #endregion

        #region Private Fields

        private readonly TextSelection _mainTextSelection;

        #endregion

        #region Constructor

        /// <summary>
        /// Initializes a new instance of the <see cref="MessageItem"/> class.
        /// Sets up focus handling, keyboard event routing, and initializes text selection.
        /// </summary>
        public MessageItem()
        {
            // Allow focus so that keyboard and selection events can be captured.
            Focusable = true;

            // Add a tunnel event handler so keydown events reach this control first.
            AddHandler(KeyDownEvent, OnKeyDownHandler, RoutingStrategies.Tunnel);

            _mainTextSelection = new TextSelection();
        }

        #endregion

        #region Rendering

        /// <summary>
        /// Renders the background, selection highlight, and text contents.
        /// </summary>
        /// <param name="context">The drawing context used for rendering.</param>
        public override void Render(DrawingContext context)
        {
            RenderBackground(context);
            _mainTextSelection.Render(context, MainTextLayout, Padding ?? new Thickness(0));
            RenderText(context);
        }

        #endregion

        #region Pointer and Input Events

        /// <summary>
        /// Handles pointer movement for text hover detection and drag-based selection updates.
        /// Changes cursor type depending on pointer position.
        /// </summary>
        /// <param name="e">The pointer event arguments.</param>
        protected override void OnPointerMoved(PointerEventArgs e)
        {
            var pointerPosition = e.GetPosition(this);
            var isPointerOverText = TextHelper.IsPointerOverText(MainTextLayout, MainTextLayoutPosition, pointerPosition);

            if (isPointerOverText)
            {
                Cursor = new Cursor(StandardCursorType.Ibeam);

                if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                {
                    var textIndex = TextHelper.GetTextIndexFromPointer(
                        MainTextLayout,
                        Text,
                        Padding ?? new Thickness(0),
                        e.GetPosition(this));

                    _mainTextSelection.End = textIndex;
                    InvalidateVisual();
                }
            }
            else
            {
                Cursor = new Cursor(StandardCursorType.Arrow);
            }
        }

        /// <summary>
        /// Handles pointer press events to initiate or modify a text selection.
        /// Supports single, double, and triple clicks for word and paragraph selection.
        /// </summary>
        /// <param name="e">The pointer pressed event arguments.</param>
        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            if (MainTextLayout == null || Text == null)
                return;

            var textIndex = TextHelper.GetTextIndexFromPointer(
                MainTextLayout,
                Text,
                Padding ?? new Thickness(0),
                e.GetPosition(this));

            _mainTextSelection.Start = textIndex;

            switch (e.ClickCount)
            {
                case 1:
                    if (_mainTextSelection.SelectedText.Length > 0)
                    {
                        _mainTextSelection.Clear();
                        InvalidateVisual();
                    }
                    break;

                case 2:
                    _mainTextSelection.SelectWordByIndex(Text, textIndex);
                    InvalidateVisual();
                    break;

                case >= 3:
                    _mainTextSelection.SelectParagraphByIndex(Text, textIndex);
                    InvalidateVisual();
                    break;
            }
        }

        /// <summary>
        /// Handles pointer release events to finalize a selection and update selected text.
        /// </summary>
        /// <param name="e">The pointer released event arguments.</param>
        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            _mainTextSelection.Update(Text);
        }

        /// <summary>
        /// Handles keyboard events for selection and copy shortcuts.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The key event arguments.</param>
        private async Task OnKeyDownAsync(object? sender, KeyEventArgs e)
        {
            // macOS keys:
            // Meta - held Command
            // LWin - left Command
            // RWin - right Command

            // CTRL+A or CMD+A: Select all text
            if (e.Key == Key.A && (e.KeyModifiers.HasFlag(KeyModifiers.Control) ||
                                   e.KeyModifiers.HasFlag(KeyModifiers.Meta)))
            {
                _mainTextSelection.SelectAll(Text);
                InvalidateVisual();
                e.Handled = true;
            }

            // CTRL+C or CMD+C: Copy selected text to clipboard
            if (e.Key == Key.C && (e.KeyModifiers.HasFlag(KeyModifiers.Control) ||
                                   e.KeyModifiers.HasFlag(KeyModifiers.Meta)))
            {
                _mainTextSelection.Update(Text);
                await CopyToClipboardAsync(_mainTextSelection.SelectedText);
            }
        }

        /// <summary>
        /// Triggers the asynchronous keydown handler in a fire-and-forget style.
        /// </summary>
        private void OnKeyDownHandler(object? sender, KeyEventArgs e)
        {
            _ = OnKeyDownAsync(sender, e);
        }

        #endregion

        #region Clipboard Handling

        /// <summary>
        /// Copies the specified text asynchronously to the system clipboard.
        /// </summary>
        /// <param name="textToCopy">The text to copy.</param>
        private async Task CopyToClipboardAsync(string textToCopy)
        {
            if (VisualRoot is TopLevel topLevel)
            {
                var clipboard = topLevel.Clipboard;
                if (clipboard == null)
                    return;

                await clipboard.SetTextAsync(textToCopy);
            }
        }

        #endregion

        #region Focus Events

        /// <summary>
        /// Called when the control gains focus. Updates the selection based on current text.
        /// </summary>
        protected override void OnGotFocus(GotFocusEventArgs e)
        {
            base.OnGotFocus(e);
            _mainTextSelection.Update(Text);
        }

        /// <summary>
        /// Called when the control loses focus.
        /// Clears selection if no context menu or flyout is open.
        /// </summary>
        protected override void OnLostFocus(RoutedEventArgs e)
        {
            base.OnLostFocus(e);

            if (ContextFlyout is not { IsOpen: true } &&
                ContextMenu is not { IsOpen: true })
            {
                _mainTextSelection.Clear();
                InvalidateVisual();
            }

            _mainTextSelection.Update(Text);
        }

        #endregion

        #region Property Changes

        /// <summary>
        /// Handles property change events to update visual state and selection brush when necessary.
        /// </summary>
        /// <param name="change">The property change event arguments.</param>
        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            switch (change.Property.Name)
            {
                // Selection color changed
                case nameof(SelectionBrush):
                    _mainTextSelection.SelectionBrush = SelectionBrush;
                    break;
            }
        }

        #endregion
    }
}

