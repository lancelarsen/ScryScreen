using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ScryScreen.App.Controls;

/// <summary>
/// Draws a filled hourglass-shaped backdrop for readability.
/// This is intentionally separate from the sand/glass rendering so opacity can be applied
/// without fading the hourglass itself.
/// </summary>
public sealed class HourglassBackdropVisual : Control
{
    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<HourglassBackdropVisual, IBrush?>(nameof(Fill), new SolidColorBrush(Color.FromArgb(170, 0, 0, 0)));

    public static readonly StyledProperty<IPen?> StrokeProperty =
        AvaloniaProperty.Register<HourglassBackdropVisual, IPen?>(nameof(Stroke));

    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public IPen? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 4 || h <= 4)
        {
            return;
        }

        // Use a geometry that matches HourglassSandVisual's outline.
        // Cell size here only affects the curvature parameters; the geometry is continuous.
        var cell = Math.Max(2.0, Math.Min(w, h) * 0.01);
        var outline = CreateHourglassOutline(w, h, cell);
        context.DrawGeometry(Fill, Stroke, outline);
    }

    private static StreamGeometry CreateHourglassOutline(double w, double h, double cell)
    {
        var cx = w / 2.0;
        var m = Math.Max(6, Math.Min(w, h) * 0.06);
        var neckH = Math.Max(cell * 2.0, h * 0.10);
        var chamberH = (h - neckH - (m * 2)) / 2.0;
        if (chamberH <= cell * 2.0)
        {
            chamberH = Math.Max(cell * 2.0, (h - (m * 2)) / 2.0);
        }

        var topY0 = m;
        var topY1 = topY0 + chamberH;
        var neckY0 = topY1;
        var neckY1 = neckY0 + neckH;
        var botY0 = neckY1;
        var botY1 = botY0 + chamberH;

        var topW = w - (m * 2);
        var botW = topW;
        var neckW = Math.Max(cell * 1.45, w * 0.035);
        var curve = Math.Min(topW, chamberH) * 0.22;

        var outline = new StreamGeometry();
        using (var g = outline.Open())
        {
            var p0 = new Point(cx - topW / 2, topY0);
            var p1 = new Point(cx + topW / 2, topY0);
            var p2 = new Point(cx + neckW / 2, topY1);
            var p3 = new Point(cx + neckW / 2, botY0);
            var p4 = new Point(cx + botW / 2, botY1);
            var p5 = new Point(cx - botW / 2, botY1);
            var p6 = new Point(cx - neckW / 2, botY0);
            var p7 = new Point(cx - neckW / 2, topY1);

            g.BeginFigure(p0, isFilled: true);
            g.LineTo(p1);
            g.CubicBezierTo(new Point(cx + topW / 2, topY0 + curve), new Point(cx + neckW / 2 + curve * 0.35, topY1 - curve), p2);
            g.LineTo(new Point(cx + neckW / 2, neckY0));
            g.LineTo(p3);
            g.CubicBezierTo(new Point(cx + neckW / 2 + curve * 0.35, botY0 + curve), new Point(cx + botW / 2, botY1 - curve), p4);
            g.LineTo(p5);
            g.CubicBezierTo(new Point(cx - botW / 2, botY1 - curve), new Point(cx - neckW / 2 - curve * 0.35, botY0 + curve), p6);
            g.LineTo(new Point(cx - neckW / 2, neckY0));
            g.LineTo(p7);
            g.CubicBezierTo(new Point(cx - neckW / 2 - curve * 0.35, topY1 - curve), new Point(cx - topW / 2, topY0 + curve), p0);
            g.EndFigure(isClosed: true);
        }

        return outline;
    }
}
