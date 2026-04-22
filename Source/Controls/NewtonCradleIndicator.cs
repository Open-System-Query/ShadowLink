using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ShadowLink.Controls;

public sealed class NewtonCradleIndicator : Control
{
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _stopwatch;

    public NewtonCradleIndicator()
    {
        Width = 132;
        Height = 54;
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
        SolidColorBrush staticBrush = ResolveBrush("AccentStrongBrush", Color.Parse("#0086B3"));
        SolidColorBrush lineBrush = ResolveBrush("BorderBrush", Color.Parse("#D6E1EA"));
        Pen linePen = new Pen(lineBrush, 2);

        Double cycleProgress = (_stopwatch.Elapsed.TotalSeconds % 1.45) / 1.45;
        Double leftAngle = 0;
        Double rightAngle = 0;

        if (cycleProgress < 0.25)
        {
            leftAngle = Lerp(-32, 0, EaseOut(cycleProgress / 0.25));
        }
        else if (cycleProgress < 0.50)
        {
            rightAngle = Lerp(0, 32, EaseInOut((cycleProgress - 0.25) / 0.25));
        }
        else if (cycleProgress < 0.75)
        {
            rightAngle = Lerp(32, 0, EaseOut((cycleProgress - 0.50) / 0.25));
        }
        else
        {
            leftAngle = Lerp(0, -32, EaseInOut((cycleProgress - 0.75) / 0.25));
        }

        Double topY = 6;
        Double stringLength = Math.Max(16, bounds.Height - 22);
        Double dotRadius = 7;
        Double spacing = 24;
        Double totalWidth = spacing * 4;
        Double startX = bounds.Width / 2 - totalWidth / 2;

        for (Int32 index = 0; index < 5; index++)
        {
            Double anchorX = startX + spacing * index;
            Double angle = index == 0 ? leftAngle : index == 4 ? rightAngle : 0;
            Point center = GetDotCenter(anchorX, topY, stringLength, angle);
            context.DrawLine(linePen, new Point(anchorX, topY), center);
            SolidColorBrush fillBrush = index == 0 || index == 4 ? accentBrush : staticBrush;
            context.DrawEllipse(fillBrush, null, center, dotRadius, dotRadius);
        }
    }

    private static Point GetDotCenter(Double anchorX, Double topY, Double stringLength, Double angle)
    {
        Double radians = angle * Math.PI / 180.0;
        Double x = anchorX + stringLength * Math.Sin(radians);
        Double y = topY + stringLength * Math.Cos(radians);
        return new Point(x, y);
    }

    private SolidColorBrush ResolveBrush(String resourceKey, Color fallbackColor)
    {
        if (this.TryGetResource(resourceKey, ActualThemeVariant, out Object? resource))
        {
            if (resource is SolidColorBrush solidColorBrush)
            {
                return solidColorBrush;
            }
        }

        return new SolidColorBrush(fallbackColor);
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

    private static Double EaseOut(Double value)
    {
        Double inverse = 1 - value;
        return 1 - inverse * inverse;
    }

    private static Double EaseInOut(Double value)
    {
        return value < 0.5
            ? 2 * value * value
            : 1 - Math.Pow(-2 * value + 2, 2) / 2;
    }

    private static Double Lerp(Double start, Double end, Double progress)
    {
        return start + (end - start) * progress;
    }
}
