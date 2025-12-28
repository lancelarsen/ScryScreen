using Avalonia;
using Avalonia.Platform;

namespace ScryScreen.App.ViewModels;

public sealed class ScreenInfoViewModel
{
    private readonly Screen _screen;

    public ScreenInfoViewModel(int index, Screen screen)
    {
        Index = index;
        _screen = screen;
    }

    public int Index { get; }

    public string DisplayName => string.IsNullOrWhiteSpace(_screen.DisplayName)
        ? $"Screen {Index + 1}"
        : _screen.DisplayName;

    public PixelRect Bounds => _screen.Bounds;

    public double Scaling => _screen.Scaling;

    public override string ToString() => DisplayName;
}
