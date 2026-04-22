using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ShadowLink.Controls;

public sealed class WindowsLoadingIndicator : Control
{
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _stopwatch;

    public WindowsLoadingIndicator()
    {
        Width = 92;
        Height = 18;
        _stopwatch = Stopwatch.StartNew();
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _timer.Tick += HandleTick;
        AttachedToVisualTree += HandleAttachedToVisualTree;
        DetachedFromVisualTree += HandleDetachedFromVisualTree;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        Rect bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        SolidColorBrush accentBrush = ResolveBrush("AccentBrush", Color.Parse("#009CD2"));
        Double cycleDurationSeconds = 1.45;
        Double dotRadius = 3.5;
        Double trackWidth = Math.Max(24, bounds.Width - dotRadius * 2);
        Double baseY = bounds.Height / 2;
        Double elapsedSeconds = _stopwatch.Elapsed.TotalSeconds;

        for (Int32 index = 0; index < 5; index++)
        {
            Double progress = ((elapsedSeconds - index * 0.12) % cycleDurationSeconds + cycleDurationSeconds) % cycleDurationSeconds / cycleDurationSeconds;
            Double eased = EaseInOut(progress);
            Double x = dotRadius + eased * trackWidth;
            Double opacity = BuildOpacity(progress);
            SolidColorBrush dotBrush = new SolidColorBrush(accentBrush.Color, opacity);
            context.DrawEllipse(dotBrush, null, new Point(x, baseY), dotRadius, dotRadius);
        }
    }

    private SolidColorBrush ResolveBrush(String resourceKey, Color fallbackColor)
    {
        if (this.TryGetResource(resourceKey, ActualThemeVariant, out Object? resource) &&
            resource is SolidColorBrush solidColorBrush)
        {
            return solidColorBrush;
        }

        return new SolidColorBrush(fallbackColor);
    }

    private static Double BuildOpacity(Double progress)
    {
        if (progress < 0.1 || progress > 0.95)
        {
            return 0;
        }

        if (progress < 0.25)
        {
            return (progress - 0.1) / 0.15;
        }

        if (progress > 0.75)
        {
            return Math.Max(0, (0.95 - progress) / 0.20);
        }

        return 1;
    }

    private void HandleTick(Object? sender, EventArgs e)
    {
        InvalidateVisual();
    }

    private void HandleAttachedToVisualTree(Object? sender, VisualTreeAttachmentEventArgs e)
    {
        _timer.Start();
    }

    private void HandleDetachedFromVisualTree(Object? sender, VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();
    }

    private static Double EaseInOut(Double value)
    {
        return value < 0.5
            ? 4 * value * value * value
            : 1 - Math.Pow(-2 * value + 2, 3) / 2;
    }
}
