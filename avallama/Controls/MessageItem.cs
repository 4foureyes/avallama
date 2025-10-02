// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

using System.Threading.Tasks;
using avallama.Utilities;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace avallama.Controls;

public class MessageItem : TextItem
{
    public static readonly StyledProperty<IBrush> SelectionBrushProperty =
        AvaloniaProperty.Register<TextItem, IBrush>(nameof(SelectionBrush));

    public IBrush SelectionBrush
    {
        get => GetValue(SelectionBrushProperty);
        set => SetValue(SelectionBrushProperty, value);
    }

    private TextSelection _mainTextSelection;

    public MessageItem()
    {
        // focusable mert azt akarjuk hogy el lehessen kapni benne a fókuszt és el is lehessen veszíteni
        Focusable = true;

        // a tunnel routingstrategies miatt tudja megkapni a keydowneventeket előbb a messageblock
        AddHandler(KeyDownEvent, OnKeyDownHandler, RoutingStrategies.Tunnel);

        _mainTextSelection = new TextSelection(SelectionBrush);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var pointerPosition = e.GetPosition(this);

        var isPointerOverText = TextHelper.IsPointerOverText(MainTextLayout, MainTextLayoutPosition, pointerPosition);
        if (isPointerOverText)
        {
            Cursor = new Cursor(StandardCursorType.Ibeam);
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                var textIndex = TextHelper.TextIndexFromPointer(MainTextLayout, Text, Padding ?? new Thickness(0),
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

    private async Task CopyToClipboardAsync(string textToCopy)
    {
        if (VisualRoot is TopLevel topLevel)
        {
            var clipboard = topLevel.Clipboard;
            if (clipboard == null) return;
            await clipboard.SetTextAsync(textToCopy);
        }
    }

    private async Task OnKeyDown(object? sender, KeyEventArgs e)
    {
        // macOS billentyűk
        // Meta - nyomva tartott Command
        // LWin - lenyomott bal oldali Command
        // RWin - lenyomott jobb oldali Command

        // CTRL+A - összes szöveg kijelölése
        if (e.Key == Key.A && (e.KeyModifiers.HasFlag(KeyModifiers.Control)
                               || e.KeyModifiers.HasFlag(KeyModifiers.Meta)))
        {
            _mainTextSelection.SelectAll(Text);
            e.Handled = true;
        }

        // CTRL+C - szöveg kimásolása vágólapra
        if (e.Key == Key.C && (e.KeyModifiers.HasFlag(KeyModifiers.Control)
                               || e.KeyModifiers.HasFlag(KeyModifiers.Meta)))
        {
            _mainTextSelection.Update(Text);
            await CopyToClipboardAsync(_mainTextSelection.SelectedText);
        }
    }

    // kattintott szó kiválasztása index alapján (pl. dupla klikkre)

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        // a kijelölés végén mentjük el hogy ne kelljen folyamatosan frissíteni a stringet
        _mainTextSelection.Update(Text);
    }

    protected override void OnGotFocus(GotFocusEventArgs e)
    {
        base.OnGotFocus(e);
        _mainTextSelection.Update(Text);
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        if (ContextFlyout is not { IsOpen: true } &&
            ContextMenu is not { IsOpen: true })
        {
            _mainTextSelection.Clear();
        }

        _mainTextSelection.Update(Text);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (MainTextLayout == null || Text == null) return;

        var textIndex =
            TextHelper.TextIndexFromPointer(MainTextLayout, Text, Padding ?? new Thickness(0), e.GetPosition(this));
        _mainTextSelection.Start = textIndex;

        switch (e.ClickCount)
        {
            case 1:
                if (_mainTextSelection.SelectedText.Length > 0)
                {
                    _mainTextSelection.Clear();
                }

                break;
            case 2:
                _mainTextSelection.SelectWordByIndex(Text, textIndex);
                break;
            case >= 3:
                _mainTextSelection.SelectParagraphByIndex(Text, textIndex);
                break;
        }
    }

    // nesze neked async
    private void OnKeyDownHandler(object? sender, KeyEventArgs e)
    {
        _ = OnKeyDown(sender, e);
    }
}
