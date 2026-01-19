using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ScryScreen.App.Controls;

public sealed class Dice3DVisual : Control
{
    public static readonly StyledProperty<int> SidesProperty =
        AvaloniaProperty.Register<Dice3DVisual, int>(nameof(Sides), 6);

    public static readonly StyledProperty<int> ValueProperty =
        AvaloniaProperty.Register<Dice3DVisual, int>(nameof(Value), 1);

    public static readonly StyledProperty<double> TiltXProperty =
        AvaloniaProperty.Register<Dice3DVisual, double>(nameof(TiltX), 0.0);

    public static readonly StyledProperty<double> TiltYProperty =
        AvaloniaProperty.Register<Dice3DVisual, double>(nameof(TiltY), 0.0);

    static Dice3DVisual()
    {
        AffectsRender<Dice3DVisual>(SidesProperty, ValueProperty, TiltXProperty, TiltYProperty, BoundsProperty);
    }

    public int Sides
    {
        get => GetValue(SidesProperty);
        set => SetValue(SidesProperty, value);
    }

    public int Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    // [-1..1] tilt to bias shading/facet direction.
    public double TiltX
    {
        get => GetValue(TiltXProperty);
        set => SetValue(TiltXProperty, value);
    }

    public double TiltY
    {
        get => GetValue(TiltYProperty);
        set => SetValue(TiltYProperty, value);
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

        var size = Math.Min(w, h);
        var cx = w * 0.5;
        var cy = h * 0.5;

        var tx = Math.Clamp(TiltX, -1, 1);
        var ty = Math.Clamp(TiltY, -1, 1);

        // Pseudo-3D depth vector (isometric-ish) + tilt bias.
        var depth = size * 0.11;
        var baseVec = new Vector(depth * 0.35, depth * 0.65);
        var tiltVec = new Vector(tx * depth * 0.45, ty * depth * 0.45);
        var dv = baseVec + tiltVec;

        // Shadow
        var shadowW = size * 0.72;
        var shadowH = size * 0.20;
        var shadowCenter = new Point(cx + dv.X * 0.25, cy + size * 0.38 + dv.Y * 0.25);
        var shadow = new RadialGradientBrush
        {
            Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(95, 0, 0, 0), 0.0),
                new GradientStop(Color.FromArgb(0, 0, 0, 0), 1.0),
            }
        };
        context.DrawEllipse(shadow, null, shadowCenter, shadowW * 0.5, shadowH * 0.5);

        var top = CreateTopFacePoints(Sides, new Point(cx, cy - size * 0.05), size * 0.42);
        var bottom = new Point[top.Length];
        for (var i = 0; i < top.Length; i++)
        {
            bottom[i] = top[i] + dv;
        }

        var outlinePen = new Pen(new SolidColorBrush(Color.FromRgb(18, 18, 18)), Math.Max(1.5, size * 0.03));

        // Side face (simple: connect the two most downward vertices)
        var (a, b) = FindMostDownEdge(top);
        var side = CreateQuad(top[a], top[b], bottom[b], bottom[a]);

        var sideBrush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromRgb(185, 185, 185), 0.0),
                new GradientStop(Color.FromRgb(120, 120, 120), 1.0),
            }
        };

        var topBrush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0.0 + (tx * 0.25), 0.0 + (ty * 0.25), RelativeUnit.Relative),
            EndPoint = new RelativePoint(1.0 + (tx * 0.25), 1.0 + (ty * 0.25), RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromRgb(252, 252, 252), 0.0),
                new GradientStop(Color.FromRgb(210, 210, 210), 1.0),
            }
        };

        context.DrawGeometry(sideBrush, outlinePen, side);
        context.DrawGeometry(topBrush, outlinePen, CreatePolygon(top));

        // Facet lines to read more like a polyhedral die.
        DrawFacetLines(context, top, outlinePen, size);

        DrawCenteredValue(context, new Rect(0, 0, w, h), Value, size);

        // Small die-type label (d20, d12, etc.) for clarity.
        DrawDieTypeBadge(context, new Rect(0, 0, w, h), Sides, size);
    }

    private static void DrawCenteredValue(DrawingContext context, Rect rect, int value, double size)
    {
        var fontSize = Math.Max(10, size * 0.34);
        var typeface = new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Bold);
        var text = new FormattedText(
            value.ToString(),
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            Brushes.Black);

        var x = rect.X + (rect.Width - text.Width) * 0.5;
        var y = rect.Y + (rect.Height - text.Height) * 0.47;

        // Subtle light halo for readability.
        var haloText = new FormattedText(
            value.ToString(),
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)));

        context.DrawText(haloText, new Point(x + 1, y + 1));
        context.DrawText(text, new Point(x, y));
    }

    private static void DrawDieTypeBadge(DrawingContext context, Rect rect, int sides, double size)
    {
        var label = "d" + Math.Max(2, sides);
        var fontSize = Math.Max(8, size * 0.16);
        var typeface = new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);
        var text = new FormattedText(
            label,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            Brushes.White);

        var pad = Math.Max(4, size * 0.08);
        var bw = text.Width + pad * 1.6;
        var bh = text.Height + pad * 0.9;
        var bx = rect.Right - bw - pad;
        var by = rect.Top + pad;
        var badgeRect = new Rect(bx, by, bw, bh);

        context.DrawRectangle(new SolidColorBrush(Color.FromArgb(170, 0, 0, 0)), null, badgeRect);
        context.DrawText(text, new Point(bx + (bw - text.Width) * 0.5, by + (bh - text.Height) * 0.5));
    }

    private static void DrawFacetLines(DrawingContext context, Point[] top, IPen outlinePen, double size)
    {
        var thin = new Pen(new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)), Math.Max(1.0, size * 0.012));
        var center = Average(top);

        // More lines for higher-sided dice (helps read as D20 vs D6).
        var spokes = top.Length switch
        {
            >= 6 => Math.Min(6, top.Length),
            5 => 5,
            4 => 4,
            _ => 3,
        };

        for (var i = 0; i < spokes; i++)
        {
            var p = top[(i * top.Length) / spokes];
            context.DrawLine(thin, center, new Point((center.X + p.X) * 0.5, (center.Y + p.Y) * 0.5));
        }

        // Outline is already drawn via the geometry stroke; this adds the internal facets.
        _ = outlinePen;
    }

    private static Point Average(Point[] pts)
    {
        var x = 0.0;
        var y = 0.0;
        foreach (var p in pts)
        {
            x += p.X;
            y += p.Y;
        }

        return new Point(x / pts.Length, y / pts.Length);
    }

    private static (int A, int B) FindMostDownEdge(Point[] poly)
    {
        var best = double.NegativeInfinity;
        var bestA = 0;
        var bestB = 1;
        for (var i = 0; i < poly.Length; i++)
        {
            var j = (i + 1) % poly.Length;
            var score = poly[i].Y + poly[j].Y;
            if (score > best)
            {
                best = score;
                bestA = i;
                bestB = j;
            }
        }

        return (bestA, bestB);
    }

    private static StreamGeometry CreatePolygon(Point[] pts)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(pts[0], isFilled: true);
            for (var i = 1; i < pts.Length; i++)
            {
                ctx.LineTo(pts[i]);
            }
            ctx.EndFigure(isClosed: true);
        }
        return g;
    }

    private static StreamGeometry CreateQuad(Point p0, Point p1, Point p2, Point p3)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(p0, isFilled: true);
            ctx.LineTo(p1);
            ctx.LineTo(p2);
            ctx.LineTo(p3);
            ctx.EndFigure(isClosed: true);
        }
        return g;
    }

    private static Point[] CreateTopFacePoints(int sides, Point center, double radius)
    {
        // D&D-ish silhouettes (not physically perfect, but recognizable at a glance).
        switch (sides)
        {
            case 4:
                return RegularPolygon(center, radius, 3, rotationRad: -Math.PI / 2);
            case 6:
                return RegularPolygon(center, radius * 0.92, 4, rotationRad: Math.PI / 4);
            case 8:
                // Diamond silhouette reads like a d8.
                return new[]
                {
                    new Point(center.X, center.Y - radius),
                    new Point(center.X + radius * 0.9, center.Y),
                    new Point(center.X, center.Y + radius),
                    new Point(center.X - radius * 0.9, center.Y),
                };
            case 10:
                // Kite-ish silhouette.
                return new[]
                {
                    new Point(center.X, center.Y - radius),
                    new Point(center.X + radius * 0.75, center.Y - radius * 0.15),
                    new Point(center.X, center.Y + radius),
                    new Point(center.X - radius * 0.75, center.Y - radius * 0.15),
                };
            case 12:
                return RegularPolygon(center, radius, 5, rotationRad: -Math.PI / 2);
            case 20:
                // Hex silhouette reads well as a d20 (icosahedron).
                return RegularPolygon(center, radius, 6, rotationRad: -Math.PI / 2);
            default:
                // Fallback: pick a reasonable top face polygon.
                var n = sides switch
                {
                    <= 4 => 3,
                    <= 8 => 4,
                    <= 12 => 5,
                    _ => 6,
                };
                return RegularPolygon(center, radius, n, rotationRad: -Math.PI / 2);
        }
    }

    private static Point[] RegularPolygon(Point center, double radius, int n, double rotationRad)
    {
        var pts = new Point[n];
        for (var i = 0; i < n; i++)
        {
            var a = rotationRad + (Math.PI * 2.0 * i / n);
            pts[i] = new Point(center.X + Math.Cos(a) * radius, center.Y + Math.Sin(a) * radius);
        }
        return pts;
    }
}
