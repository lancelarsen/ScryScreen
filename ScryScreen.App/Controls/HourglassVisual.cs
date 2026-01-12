using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ScryScreen.App.Controls;

public sealed class HourglassVisual : Control
{
    public static readonly StyledProperty<double> FractionRemainingProperty =
        AvaloniaProperty.Register<HourglassVisual, double>(nameof(FractionRemaining), 1.0);

    public double FractionRemaining
    {
        get => GetValue(FractionRemainingProperty);
        set => SetValue(FractionRemainingProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 4 || h <= 4)
        {
            return;
        }

        var frac = FractionRemaining;
        if (double.IsNaN(frac) || double.IsInfinity(frac)) frac = 0;
        if (frac < 0) frac = 0;
        if (frac > 1) frac = 1;

        var m = Math.Max(2, Math.Min(w, h) * 0.06);
        var neckH = Math.Max(4, h * 0.10);
        var chamberH = (h - neckH - (m * 2)) / 2.0;
        if (chamberH <= 2)
        {
            chamberH = Math.Max(2, (h - (m * 2)) / 2.0);
        }

        var topY0 = m;
        var topY1 = topY0 + chamberH;
        var neckY0 = topY1;
        var neckY1 = neckY0 + neckH;
        var botY0 = neckY1;
        var botY1 = botY0 + chamberH;

        var cx = w / 2.0;
        var topW = w - (m * 2);
        var botW = topW;
        var neckW = Math.Max(6, w * 0.10);

        var outline = new StreamGeometry();
        using (var g = outline.Open())
        {
            // Outer outline: top trapezoid -> waist -> bottom trapezoid.
            g.BeginFigure(new Point(cx - topW / 2, topY0), isFilled: false);
            g.LineTo(new Point(cx + topW / 2, topY0));
            g.LineTo(new Point(cx + neckW / 2, topY1));
            g.LineTo(new Point(cx + neckW / 2, neckY0));
            g.LineTo(new Point(cx + botW / 2, botY0));
            g.LineTo(new Point(cx + botW / 2, botY1));
            g.LineTo(new Point(cx - botW / 2, botY1));
            g.LineTo(new Point(cx - botW / 2, botY0));
            g.LineTo(new Point(cx - neckW / 2, neckY0));
            g.LineTo(new Point(cx - neckW / 2, topY1));
            g.EndFigure(isClosed: true);
        }

        var stroke = new Pen(new SolidColorBrush(Color.FromArgb(210, 232, 217, 182)), thickness: Math.Max(1, Math.Min(w, h) * 0.03));
        context.DrawGeometry(null, stroke, outline);

        // Glass tint.
        var glass = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));
        context.DrawGeometry(glass, null, outline);

        // Sand fill areas.
        var sandBrush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = new GradientStops
            {
                new GradientStop(Color.FromArgb(220, 244, 210, 120), 0),
                new GradientStop(Color.FromArgb(220, 232, 160, 72), 1),
            }
        };

        // Top chamber clip.
        var topClip = new StreamGeometry();
        using (var g = topClip.Open())
        {
            g.BeginFigure(new Point(cx - topW / 2, topY0), isFilled: true);
            g.LineTo(new Point(cx + topW / 2, topY0));
            g.LineTo(new Point(cx + neckW / 2, topY1));
            g.LineTo(new Point(cx - neckW / 2, topY1));
            g.EndFigure(isClosed: true);
        }

        // Bottom chamber clip.
        var botClip = new StreamGeometry();
        using (var g = botClip.Open())
        {
            g.BeginFigure(new Point(cx - neckW / 2, botY0), isFilled: true);
            g.LineTo(new Point(cx + neckW / 2, botY0));
            g.LineTo(new Point(cx + botW / 2, botY1));
            g.LineTo(new Point(cx - botW / 2, botY1));
            g.EndFigure(isClosed: true);
        }

        // Fill top: remaining sand.
        var topFillHeight = chamberH * frac;
        using (context.PushGeometryClip(topClip))
        {
            var fillRect = new Rect(m, topY1 - topFillHeight, w - (m * 2), topFillHeight);
            context.DrawRectangle(sandBrush, null, fillRect);
        }

        // Fill bottom: accumulated sand.
        var botFillHeight = chamberH * (1 - frac);
        using (context.PushGeometryClip(botClip))
        {
            var fillRect = new Rect(m, botY1 - botFillHeight, w - (m * 2), botFillHeight);
            context.DrawRectangle(sandBrush, null, fillRect);
        }

        // Simple falling stream while not empty.
        if (frac > 0 && frac < 1)
        {
            var streamPen = new Pen(new SolidColorBrush(Color.FromArgb(200, 232, 160, 72)), thickness: Math.Max(1, w * 0.02));
            context.DrawLine(streamPen, new Point(cx, neckY0 + 1), new Point(cx, neckY1 - 1));
        }
    }
}
