// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace avallama.Views;

/// <summary>
/// Represents the main window of the application.
/// Handles custom window chrome, drag interactions, and dynamic resizing based on the current view model.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class.
    /// Sets up OS-specific window chrome configurations.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // windows specific settings: remove the default title bar
            ExtendClientAreaToDecorationsHint = true;
            ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome;
            ExtendClientAreaTitleBarHeightHint = 0;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // remove title bar on macos, but keep the native window control buttons in the top left corner
            ExtendClientAreaToDecorationsHint = true;
            ReplaceCanvasWithGrid();
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // replace canvas with grid to fix layout rendering on linux
            ReplaceCanvasWithGrid();
        }
    }

    /// <summary>
    /// Replaces the root Canvas with a Grid to resolve layout behavior differences on macOS and Linux.
    /// </summary>
    private void ReplaceCanvasWithGrid()
    {
        // create a new grid which will act as the window content
        var grid = new Grid();

        // extract the content control which is the second child in the original canvas
        var contentControl = MainCanvas.Children[1];

        MainCanvas.Children.Clear();
        grid.Children.Add(contentControl);

        Content = grid;
    }

    /// <summary>
    /// Allows dragging the custom window when pressing the left mouse button on the top area.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The routed event arguments.</param>
    private void Window_PointerPressed(object? sender, RoutedEventArgs e)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            if (e is not PointerPressedEventArgs args) return;

            var positionY = args.GetPosition(this).Y;

            // the window can only be moved if clicked within the top 30 pixels (custom title bar area)
            if (positionY < 30)
            {
                BeginMoveDrag(args);
            }
        }
    }

    /// <summary>
    /// Closes the application window.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The routed event arguments.</param>
    private void CloseButton_OnClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    /// <summary>
    /// Toggles the window state between maximized and normal, and updates the icon.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The routed event arguments.</param>
    private void MinMaxButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var svgIcon = this.FindControl<Avalonia.Svg.Svg>("MinMaxSvg");

        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
            if (svgIcon != null) svgIcon.Path = "/Assets/Svg/maximize.svg";
        }
        else
        {
            WindowState = WindowState.Maximized;
            if (svgIcon != null) svgIcon.Path = "/Assets/Svg/minimize.svg";
        }
    }

    /// <summary>
    /// Minimizes the application window to the taskbar or dock.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The routed event arguments.</param>
    private void HideButton_OnClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }
}
