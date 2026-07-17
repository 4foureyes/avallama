// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

using avallama.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace avallama.Views;

public partial class ConversationView : UserControl
{
    public ConversationView()
    {
        InitializeComponent();

        // we handle pointerwheel scrolls globally, if not the scrollviewer would catch it
        // and if not handled separately, the scroll-to-bottom would appear when a new message is added as the scrollbar grows
        AddHandler(PointerWheelChangedEvent, OnGlobalPointerWheelChanged, RoutingStrategies.Tunnel);
    }

    private string _scrollSetting = string.Empty;
    private bool _userScrolledWithWheel;

    private void ScrollViewer_OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_scrollSetting is "" or null)
        {
            if (DataContext is not ConversationViewModel vm || vm.ScrollSetting == "")
            {
                _scrollSetting = "float";
            }
            else
            {
                _scrollSetting = vm.ScrollSetting;
            }
        }

        var scrollViewer = sender as ScrollViewer;
        if (_scrollSetting == "auto")
        {
            if (!(e.ExtentDelta.Y > 0)) return;
            scrollViewer?.ScrollToEnd();
        }
        else if (_scrollSetting == "float")
        {
            // scroll to bottom button appears when scrolling down
            if (e.OffsetDelta.Y > 10 && !ScrollToBottomBtn.IsVisible && _userScrolledWithWheel)
            {
                ScrollToBottomBtn.IsVisible = true;
                ScrollToBottomBtnShadow.IsVisible = true;
                ScrollToBottomBtnShadow.BoxShadow = new BoxShadows
                (
                    new BoxShadow
                    {
                        OffsetY = 3,
                        Blur = 20,
                        Color = new Color(120, 0, 0, 0),
                        Spread = 5
                    }
                );
            }
            // scroll up somewhat OR scroll down to the bottom AND user scrolled with wheel, so message generation didn't move the scrollbar
            else if (e.OffsetDelta.Y < 0 || scrollViewer?.Offset.Y + scrollViewer?.Viewport.Height >=
                     scrollViewer?.Extent.Height - 1
                     && _userScrolledWithWheel && ScrollToBottomBtn.IsVisible)
            {
                ScrollToBottomBtn.IsVisible = false;
                ScrollToBottomBtnShadow.IsVisible = false;
            }

            _userScrolledWithWheel = false;
        }
    }

    private void OnGlobalPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        _userScrolledWithWheel = true;
    }

    private void ScrollToBottomBtn_OnClick(object? sender, RoutedEventArgs e)
    {
        MessagesScrollViewer.ScrollToEnd();
        ScrollToBottomBtn.IsVisible = false;
        ScrollToBottomBtnShadow.IsVisible = false;
    }
}

