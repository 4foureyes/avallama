// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

using avallama.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace avallama.Views;

public partial class ConversationView : UserControl
{
    public ConversationView()
    {
        InitializeComponent();
    }

    private const string ScrollFloatKey = "float";
    private const string ScrollAutoKey = "auto";

    private void ScrollViewer_OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer) return;

        var scrollSetting = (DataContext as ConversationViewModel)?.ScrollSetting ?? ScrollAutoKey;
        var isAtBottom = scrollViewer.Offset.Y + scrollViewer.Viewport.Height >= scrollViewer.Extent.Height - 5;

        // height got increased (e.g. new content is added)
        if (e.ExtentDelta.Y > 0)
        {
            if (scrollSetting == ScrollAutoKey)
            {
                Dispatcher.UIThread.Post(() => { scrollViewer.ScrollToEnd(); });
            }
        }

        if (scrollSetting == ScrollFloatKey)
        {
            if (isAtBottom)
            {
                if (!ScrollToBottomBtn.IsVisible) return;

                ScrollToBottomBtn.IsVisible = false;
                ScrollToBottomBtnShadow.IsVisible = false;
            }
            else
            {
                if (ScrollToBottomBtn.IsVisible) return;

                ScrollToBottomBtn.IsVisible = true;
                ScrollToBottomBtnShadow.IsVisible = true;

                ScrollToBottomBtnShadow.BoxShadow = new BoxShadows(
                    new BoxShadow
                    {
                        OffsetY = 3,
                        Blur = 20,
                        Color = new Color(120, 0, 0, 0),
                        Spread = 5
                    }
                );
            }
        }
    }

    private void ScrollToBottomBtn_OnClick(object? sender, RoutedEventArgs e)
    {
        MessagesScrollViewer.ScrollToEnd();
        ScrollToBottomBtn.IsVisible = false;
        ScrollToBottomBtnShadow.IsVisible = false;
    }
}
