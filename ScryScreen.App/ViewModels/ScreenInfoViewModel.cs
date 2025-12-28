using Avalonia;
using Avalonia.Platform;

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

    public override string ToString() => DisplayName;
}
