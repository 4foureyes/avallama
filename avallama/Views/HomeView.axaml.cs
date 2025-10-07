// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

using System;
using System.Linq;
using System.Runtime.InteropServices;
using avallama.Services;
using avallama.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace avallama.Views;

public partial class HomeView : UserControl
{
    public HomeView()
    {
        InitializeComponent();

        // focusable, hogy ha a messageblockban van kijelölés akkor átadhassa a homeviewnak a fókuszt ha kikattintanak a messageblockból
        // és így a kijelölés törölhető
        Focusable = true;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // margin beállítás hogy legyen az ablakkezelő gomboknak helye macOS-en
            SetMacOSMargin();
        }
        else
        {
            SideBarTopGrid.Margin = new Thickness(14,14,14,0);
            SideBarButton.Margin = new Thickness(0,-10,0,0);
        }

        // globálisan figyelünk a pointerwheeles görgetésre, különben a scrollviewer elkapná
        // és ha nem lenne erre külön figyelve akkor új üzenet hozzáadásnál is mivel scrollbar növekszik megjelenne a scroll-to-bottom gomb
        AddHandler(PointerWheelChangedEvent, OnGlobalPointerWheelChanged, RoutingStrategies.Tunnel);
    }

    private bool _sideBarExpanded = true;
    private double _sideBarWidth;
    private Control? _sideBarControl;
    private string _scrollSetting = string.Empty;
    private bool _userScrolledWithWheel;

    // megnézi hogy milyen állapotban van az ablak és a sidebar
    // majd eszerint beállítja a marginjukat, hogy macOS-en az ablakkezelő gombok használhatóak legyenek
    private void SetMacOSMargin()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return;

        // ha a natív macOS ablak full screenben van
        // Avaloniával ezt nem lehet elérni szóval natív külső könyvtár kellett hozzá (lásd: MacOSInterop.cs)
        if (MacOSInterop.isKeyWindowInFullScreen())
        {
            SideBarTopGrid.Margin = new Thickness(14,14,14,0);
            SideBarButton.Margin = _sideBarExpanded ? new Thickness(0, -10, 0, 0) : new Thickness(10, -10, 0, 0);
        }
        else
        {
            if (_sideBarExpanded)
            {
                SideBarTopGrid.Margin = new Thickness(14, 30, 14, 0);
                SideBarButton.Margin = new Thickness(0, -10, 0, 0);
            }
            else
            {
                SideBarButton.Margin = new Thickness(10, 26, 0, 0);
            }
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        SetMacOSMargin();
    }

    private void ScrollViewer_OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_scrollSetting is "" or null)
        {
            if (DataContext is not HomeViewModel vm || vm.ScrollSetting == "")
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
            // legörgetésnél megjelenik a scroll to bottom gomb
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
            // felgörgetés valamennyit VAGY teljesen legörgetés az aljára ÉS felhasználói görgetés tehát nem üzenet generálás mozdítja a scrollbart
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
        ConversationScrollViewer.ScrollToEnd();
        ScrollToBottomBtn.IsVisible = false;
        ScrollToBottomBtnShadow.IsVisible = false;
    }

    private void SideBarBtn_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_sideBarExpanded)
        {
            _sideBarControl = SideBar;
            MainGrid.Children.RemoveAt(0);
            var columnDefinitions = new ColumnDefinitions
            {
                // sidebar
                new ColumnDefinition(new GridLength(0, GridUnitType.Pixel)),
                // gridsplitter
                new ColumnDefinition(new GridLength(0, GridUnitType.Pixel)),
                // sidebar expand/hide gomb
                new ColumnDefinition(new GridLength(50, GridUnitType.Pixel)),
                // chat rész
                new ColumnDefinition(new GridLength(7, GridUnitType.Star))
            };

            MainGrid.ColumnDefinitions = columnDefinitions;
            _sideBarExpanded = false;

            SideBarButton.Margin = new Thickness(10,-10,0,0);
        }
        else
        {
            if (_sideBarControl == null) return;
            var columnDefinitions = new ColumnDefinitions
            {
                // sidebar
                new ColumnDefinition(new GridLength(_sideBarWidth, GridUnitType.Pixel))
                    { MinWidth = 250, MaxWidth = 400 },
                // gridsplitter
                new ColumnDefinition(new GridLength(8, GridUnitType.Pixel)),
                // sidebar expand/hide gomb
                new ColumnDefinition(new GridLength(50, GridUnitType.Pixel)),
                // chat rész
                new ColumnDefinition(new GridLength(7, GridUnitType.Star))
            };
            MainGrid.ColumnDefinitions = columnDefinitions;
            MainGrid.Children.Insert(0, _sideBarControl);
            _sideBarExpanded = true;
        }

        SetMacOSMargin();
    }

    private void SideBar_OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        // ha átméretezi a sidebart akkor beállítjuk reszponzívan az új csevegés gomb szövegét
        _sideBarWidth = SideBar.Bounds.Width;

        // textblock lekérése közvetlenül hogy tudjuk módosítani a láthatóságot és így középre tud igazodni az ikon
        var buttonTextBlock = NewConversationBtn.GetTemplateChildren().FirstOrDefault(c => c is TextBlock) as TextBlock;
        switch (_sideBarWidth)
        {
            case < 300:
                if (buttonTextBlock != null) buttonTextBlock.IsVisible = false;
                NewConversationBtn.Content = string.Empty;
                break;
            case >= 300:
                if (buttonTextBlock != null) buttonTextBlock.IsVisible = true;
                NewConversationBtn.Content = LocalizationService.GetString("NEW");
                break;
        }
    }
}
