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

namespace avallama.Controls;

public class ConversationItem : TextItem
{
    public static readonly StyledProperty<IBrush?> HoverBackgroundProperty =
        AvaloniaProperty.Register<ConversationItem, IBrush?>(nameof(HoverBackground));

    public static readonly StyledProperty<IBrush?> ActiveBackgroundProperty =
        AvaloniaProperty.Register<ConversationItem, IBrush?>(nameof(ActiveBackground));

    public static readonly StyledProperty<IBrush?> HoverTextColorProperty =
        AvaloniaProperty.Register<ConversationItem, IBrush?>(nameof(HoverTextColor));

    public static readonly StyledProperty<IBrush?> ActiveTextColorProperty =
        AvaloniaProperty.Register<ConversationItem, IBrush?>(nameof(ActiveTextColor));

    public static readonly StyledProperty<IBrush?> HoverSubTextColorProperty =
        AvaloniaProperty.Register<ConversationItem, IBrush?>(nameof(HoverSubTextColor));

    public static readonly StyledProperty<IBrush?> ActiveSubTextColorProperty =
        AvaloniaProperty.Register<ConversationItem, IBrush?>(nameof(ActiveSubTextColor));

    // saját ID
    public static readonly DirectProperty<ConversationItem, Guid?> IdProperty =
        AvaloniaProperty.RegisterDirect<ConversationItem, Guid?>(
            nameof(Id),
            o => o.Id,
            (o, v) => o.Id = v,
            unsetValue: Guid.Empty
        );

    // a jelenleg kiválasztott ID, ez összehasonlításhoz kell, hogy más stílust lehessen beállítani ha a kiválasztott control a jelenlegi
    public static readonly DirectProperty<ConversationItem, Guid?> SelectedIdProperty =
        AvaloniaProperty.RegisterDirect<ConversationItem, Guid?>(
            nameof(SelectedId),
            o => o.SelectedId,
            (o, v) => o.SelectedId = v,
            unsetValue: Guid.Empty
        );

    public static readonly StyledProperty<ICommand?> CommandProperty =
        AvaloniaProperty.Register<TextItem, ICommand?>(nameof(Command));

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

    public Guid? Id
    {
        get => _id;
        set => SetAndRaise(IdProperty, ref _id, value);
    }

    private Guid? _selectedId;

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

    private bool _isPointerOver;

    protected override void RenderBackground(DrawingContext context)
    {
        // Ha van háttér megadva akkor lerendereljük a CornerRadius alapján (ami lehet 0 is)
        var background = Background;
        if (Id != null && SelectedId != null)
        {
            if (Id == SelectedId)
            {
                background = ColorProvider.GetColor(AppColor.PrimaryContainer);
            }
            else if (Id != SelectedId && _isPointerOver)
            {
                background = ColorProvider.GetColor(AppColor.SecondaryContainer);
            }
        }

        if (background == null) return;

        var cornerRadius = CornerRadius ?? new CornerRadius(0, 0, 0, 0);
        context.DrawRectangle(background, null,
            new RoundedRect(
                new Rect(Bounds.Size),
                cornerRadius.TopLeft,
                cornerRadius.TopRight,
                cornerRadius.BottomRight,
                cornerRadius.BottomLeft
            )
        );
    }

    protected override TextLayout? CreateTextLayout()
    {
        if (string.IsNullOrEmpty(Text)) return null;

        // typeface beállítása
        var typeface = new Typeface(
            FontFamily ?? FontFamily.Default
        );

        var textColor = TextColor;
        if (Id != null && SelectedId != null && Id == SelectedId)
        {
            if (Id == SelectedId)
            {
                textColor = ActiveTextColor;
            }
            else if (Id != SelectedId && _isPointerOver)
            {
                textColor = HoverTextColor;
            }
        }

        // real-time generálásnál képes több ezres nagyságban létrehozni és felszabadítani TextLayout elemeket
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

    protected override TextLayout? CreateSubTextLayout()
    {
        if (string.IsNullOrEmpty(SubText)) return null;

        var subTextColor = SubTextColor;
        if (Id != null && SelectedId != null)
        {
            if (Id == SelectedId)
            {
                subTextColor = ActiveSubTextColor;
            }
            else if (Id != SelectedId && _isPointerOver)
            {
                subTextColor = HoverSubTextColor;
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

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        Cursor = new Cursor(StandardCursorType.Hand);
        _isPointerOver = true;

        // tooltip megjelenítése ha a szöveg le van vágva
        if (MainTextLayout != null && MainTextLayout.TextLines[0].HasCollapsed)
        {
            var titleToolTip = new ToolTip { Content = Text };
            ToolTip.SetTip(this, titleToolTip);
        }

        InvalidateTextLayouts();
        CreateTextLayouts();
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        _isPointerOver = false;
        ToolTip.SetTip(this, null);
        InvalidateTextLayouts();
        CreateTextLayouts();
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (Id is null) return;
        if (Command is not null && Command.CanExecute(Id))
        {
            Command.Execute(Id);
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        switch (change.Property.Name)
        {
            case nameof(Id):
            case nameof(SelectedId):
            {
                InvalidateTextLayouts();
                CreateTextLayouts();
                InvalidateVisual();
                break;
            }
        }
    }
}
