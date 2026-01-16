using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ScryScreen.App.Controls;

public sealed class MapRevealMask : Control
{
    public static readonly StyledProperty<double> OverlayOpacityProperty =
        AvaloniaProperty.Register<MapRevealMask, double>(nameof(OverlayOpacity), 0.85);

    public static readonly StyledProperty<double> RevealXProperty =
        AvaloniaProperty.Register<MapRevealMask, double>(nameof(RevealX), 0.25);

    public static readonly StyledProperty<double> RevealYProperty =
        AvaloniaProperty.Register<MapRevealMask, double>(nameof(RevealY), 0.25);

    public static readonly StyledProperty<double> RevealWidthProperty =
        AvaloniaProperty.Register<MapRevealMask, double>(nameof(RevealWidth), 0.5);

    public static readonly StyledProperty<double> RevealHeightProperty =
        AvaloniaProperty.Register<MapRevealMask, double>(nameof(RevealHeight), 0.5);

    static MapRevealMask()
    {
        AffectsRender<MapRevealMask>(
            OverlayOpacityProperty,
            RevealXProperty,
            RevealYProperty,
            RevealWidthProperty,
            RevealHeightProperty);
    }

    public double OverlayOpacity
    {
        get => GetValue(OverlayOpacityProperty);
        set => SetValue(OverlayOpacityProperty, value);
    }

    public double RevealX
    {
        get => GetValue(RevealXProperty);
        set => SetValue(RevealXProperty, value);
    }

    public double RevealY
    {
        get => GetValue(RevealYProperty);
        set => SetValue(RevealYProperty, value);
    }

    public double RevealWidth
    {
        get => GetValue(RevealWidthProperty);
        set => SetValue(RevealWidthProperty, value);
    }

    public double RevealHeight
    {
        get => GetValue(RevealHeightProperty);
        set => SetValue(RevealHeightProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0)
        {
            return;
        }

        var opacity = Clamp(OverlayOpacity, 0.0, 1.0);
        if (opacity <= 0.0001)
        {
            return;
        }

        var brush = new SolidColorBrush(Colors.Black, opacity);

        var holeX = Clamp(RevealX, 0.0, 1.0) * w;
        var holeY = Clamp(RevealY, 0.0, 1.0) * h;
        var holeW = Clamp(RevealWidth, 0.0, 1.0) * w;
        var holeH = Clamp(RevealHeight, 0.0, 1.0) * h;

        if (holeW <= 1 || holeH <= 1)
        {
            context.FillRectangle(brush, new Rect(0, 0, w, h));
            return;
        }

        var hole = new Rect(holeX, holeY, holeW, holeH);
        hole = hole.Intersect(new Rect(0, 0, w, h));

        if (hole.Width <= 0 || hole.Height <= 0)
        {
            context.FillRectangle(brush, new Rect(0, 0, w, h));
            return;
        }

        // Top
        if (hole.Y > 0)
        {
            context.FillRectangle(brush, new Rect(0, 0, w, hole.Y));
        }

        // Left
        if (hole.X > 0)
        {
            context.FillRectangle(brush, new Rect(0, hole.Y, hole.X, hole.Height));
        }

        // Right
        if (hole.Right < w)
        {
            context.FillRectangle(brush, new Rect(hole.Right, hole.Y, w - hole.Right, hole.Height));
        }

        // Bottom
        if (hole.Bottom < h)
        {
            context.FillRectangle(brush, new Rect(0, hole.Bottom, w, h - hole.Bottom));
        }
    }

    private static double Clamp(double value, double min, double max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }
}
