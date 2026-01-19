using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ScryScreen.App.Controls;

public sealed class DiceFaceVisual : Control
{
    public static readonly StyledProperty<int> ValueProperty =
        AvaloniaProperty.Register<DiceFaceVisual, int>(nameof(Value), 1);

    public static readonly StyledProperty<int> SidesProperty =
        AvaloniaProperty.Register<DiceFaceVisual, int>(nameof(Sides), 6);

    static DiceFaceVisual()
    {
        AffectsRender<DiceFaceVisual>(ValueProperty, SidesProperty, BoundsProperty);
    }

    public int Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public int Sides
    {
        get => GetValue(SidesProperty);
        set => SetValue(SidesProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 1 || h <= 1)
        {
            return;
        }

        var rect = new Rect(0, 0, w, h);

        // Die body
        var fill = new SolidColorBrush(Color.FromRgb(245, 245, 245));
        var stroke = new SolidColorBrush(Color.FromRgb(40, 40, 40));
        context.DrawRectangle(fill, new Pen(stroke, 2), rect);

        if (Sides == 6 && Value is >= 1 and <= 6)
        {
            DrawD6Pips(context, rect, Value);
            return;
        }

        DrawCenteredNumber(context, rect, Value);
    }

    private static void DrawCenteredNumber(DrawingContext context, Rect rect, int value)
    {
        var fontSize = Math.Min(rect.Width, rect.Height) * 0.45;
        var typeface = new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Bold);

        var text = new FormattedText(
            value.ToString(),
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            Brushes.Black);

        var x = rect.X + (rect.Width - text.Width) * 0.5;
        var y = rect.Y + (rect.Height - text.Height) * 0.5;
        context.DrawText(text, new Point(x, y));
    }

    private static void DrawD6Pips(DrawingContext context, Rect rect, int value)
    {
        var pipBrush = Brushes.Black;
        var s = Math.Min(rect.Width, rect.Height);
        var pipR = s * 0.07;

        // Positions on a 3x3 grid.
        Point P(double gx, double gy)
            => new(rect.X + rect.Width * gx, rect.Y + rect.Height * gy);

        var tl = P(0.28, 0.28);
        var tr = P(0.72, 0.28);
        var bl = P(0.28, 0.72);
        var br = P(0.72, 0.72);
        var ml = P(0.28, 0.50);
        var mr = P(0.72, 0.50);
        var c = P(0.50, 0.50);

        void Pip(Point p) => context.DrawEllipse(pipBrush, null, p, pipR, pipR);

        switch (value)
        {
            case 1:
                Pip(c);
                break;
            case 2:
                Pip(tl);
                Pip(br);
                break;
            case 3:
                Pip(tl);
                Pip(c);
                Pip(br);
                break;
            case 4:
                Pip(tl);
                Pip(tr);
                Pip(bl);
                Pip(br);
                break;
            case 5:
                Pip(tl);
                Pip(tr);
                Pip(c);
                Pip(bl);
                Pip(br);
                break;
            case 6:
                Pip(tl);
                Pip(tr);
                Pip(ml);
                Pip(mr);
                Pip(bl);
                Pip(br);
                break;
        }
    }
}
