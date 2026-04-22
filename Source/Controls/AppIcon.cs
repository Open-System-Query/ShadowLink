using System;
using Avalonia;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace ShadowLink.Controls;

public enum AppIconKind
{
    Desktop = 0,
    Keyboard = 1,
    Search = 2,
    Plug = 3,
    Bolt = 4,
    Gear = 5,
    Clock = 6
}

public sealed class AppIcon : Path
{
    public static readonly StyledProperty<AppIconKind> KindProperty =
        AvaloniaProperty.Register<AppIcon, AppIconKind>(nameof(Kind));

    static AppIcon()
    {
        KindProperty.Changed.AddClassHandler<AppIcon>((icon, _) => icon.UpdateIcon());
    }

    public AppIcon()
    {
        Stretch = Stretch.Uniform;
        StrokeThickness = 1.9;
        StrokeLineCap = PenLineCap.Round;
        StrokeJoin = PenLineJoin.Round;
        Width = 20;
        Height = 20;
        UpdateIcon();
    }

    public AppIconKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == StrokeProperty && Stroke is null)
        {
            Stroke = Brushes.Black;
        }
    }

    private void UpdateIcon()
    {
        Data = Geometry.Parse(GetGeometry(Kind));

        if (Stroke is null)
        {
            Stroke = Brushes.Black;
        }
    }

    private static String GetGeometry(AppIconKind kind)
    {
        return kind switch
        {
            AppIconKind.Desktop => "M3,5 L21,5 L21,15 L3,15 Z M9,19 L15,19 M12,15 L12,19",
            AppIconKind.Keyboard => "M3,7 L21,7 L21,17 L3,17 Z M6,10 L7,10 M9,10 L10,10 M12,10 L13,10 M15,10 L16,10 M18,10 L19,10 M6,13 L7,13 M9,13 L10,13 M12,13 L13,13 M15,13 L19,13",
            AppIconKind.Search => "M10,4 A6,6 0 1 1 9.99,4 M14.5,14.5 L20,20",
            AppIconKind.Plug => "M9,3 L9,8 M15,3 L15,8 M8,8 L16,8 L16,12 C16,14.2 14.2,16 12,16 C9.8,16 8,14.2 8,12 Z M12,16 L12,21",
            AppIconKind.Bolt => "M13,2 L6,13 L11,13 L10,22 L18,10 L13,10 Z",
            AppIconKind.Gear => "M12,4 L12,2 M12,22 L12,20 M4,12 L2,12 M22,12 L20,12 M6.3,6.3 L4.9,4.9 M19.1,19.1 L17.7,17.7 M17.7,6.3 L19.1,4.9 M4.9,19.1 L6.3,17.7 M12,8 A4,4 0 1 1 11.99,8",
            AppIconKind.Clock => "M12,4 A8,8 0 1 1 11.99,4 M12,8 L12,12 L16,14",
            _ => "M3,5 L21,5 L21,15 L3,15 Z M9,19 L15,19 M12,15 L12,19"
        };
    }
}
