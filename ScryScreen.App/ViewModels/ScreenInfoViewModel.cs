using Avalonia;
using Avalonia.Platform;
using System;

namespace ScryScreen.App.ViewModels;

public sealed class ScreenInfoViewModel
{
    private readonly Screen _screen;
    private readonly int _totalScreens;

    public ScreenInfoViewModel(int index, Screen screen, int totalScreens)
    {
        Index = index;
        _screen = screen;
        _totalScreens = totalScreens;
    }

    public int Index { get; }

    public bool IsPrimary => _screen.IsPrimary;

    public string DisplayName
    {
        get
        {
            var baseName = string.IsNullOrWhiteSpace(_screen.DisplayName)
                ? $"Screen {Index + 1}"
                : _screen.DisplayName;

            if (!IsPrimary)
            {
                return baseName;
            }

            return "Primary";
        }
    }

    public PixelRect Bounds => _screen.Bounds;

    public double Scaling => _screen.Scaling;

    public int WidthPx => Bounds.Width;

    public int HeightPx => Bounds.Height;

    public string ResolutionText => $"{WidthPx}×{HeightPx}";

    public string ScalingText
    {
        get
        {
            var scaling = Scaling <= 0 ? 1.0 : Scaling;
            var percent = (int)Math.Round(scaling * 100);
            return $"{percent}%";
        }
    }

    public double AspectRatio => HeightPx == 0 ? 0 : (double)WidthPx / HeightPx;

    public string AspectRatioText
    {
        get
        {
            if (WidthPx <= 0 || HeightPx <= 0)
            {
                return string.Empty;
            }

            // Common-ish friendly labels; otherwise fall back to numeric ratio.
            var r = AspectRatio;
            if (Math.Abs(r - (16.0 / 9.0)) < 0.02) return "16:9";
            if (Math.Abs(r - (21.0 / 9.0)) < 0.03) return "21:9";
            if (Math.Abs(r - (16.0 / 10.0)) < 0.03) return "16:10";
            if (Math.Abs(r - (4.0 / 3.0)) < 0.03) return "4:3";

            return $"{r:0.00}:1";
        }
    }

    public string DisplayLabel
    {
        get
        {
            var aspect = string.IsNullOrWhiteSpace(AspectRatioText) ? string.Empty : $" ({AspectRatioText})";
            return $"{DisplayName} — {ResolutionText}{aspect} @ {ScalingText}";
        }
    }

    public override string ToString() => DisplayName;
}
