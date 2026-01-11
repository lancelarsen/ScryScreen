using Avalonia;
using ScryScreen.App.Utilities;

namespace ScryScreen.Tests;

public sealed class ScreenMatchingTests
{
    private sealed class Marker
    {
        public Marker(string name) => Name = name;
        public string Name { get; }
        public override string ToString() => Name;
    }

    private static (ScreenMatchCandidate Candidate, Marker Value) C(string name, int x, int y, int w, int h, double scaling = 1.0, bool primary = false, string? platformName = null)
    {
        return (
            new ScreenMatchCandidate(
                PlatformDisplayName: platformName ?? string.Empty,
                Bounds: new PixelRect(x, y, w, h),
                Scaling: scaling,
                IsPrimary: primary),
            new Marker(name));
    }

    [Fact]
    public void FindBestMatch_PrefersPlatformDisplayName_WhenPresent()
    {
        var prev = new ScreenMatchCandidate(
            PlatformDisplayName: "\\\\.\\DISPLAY2",
            Bounds: new PixelRect(0, 0, 1920, 1080),
            Scaling: 1.0,
            IsPrimary: false);

        var a = C("A", 0, 0, 1920, 1080, platformName: "\\\\.\\DISPLAY1", primary: true);
        var b = C("B", 1920, 0, 1920, 1080, platformName: "\\\\.\\DISPLAY2");

        var match = ScreenMatching.FindBestMatch(prev, new[] { a, b });
        Assert.NotNull(match);
        Assert.Equal("B", match!.Name);
    }

    [Fact]
    public void FindBestMatch_FallsBackToBoundsMatch_WhenNameMissing()
    {
        var prev = new ScreenMatchCandidate(
            PlatformDisplayName: "",
            Bounds: new PixelRect(1920, 0, 2560, 1440),
            Scaling: 1.25,
            IsPrimary: false);

        var a = C("A", 0, 0, 1920, 1080, scaling: 1.0, primary: true);
        var b = C("B", 1920, 0, 2560, 1440, scaling: 1.25);

        var match = ScreenMatching.FindBestMatch(prev, new[] { a, b });
        Assert.NotNull(match);
        Assert.Equal("B", match!.Name);
    }

    [Fact]
    public void FindBestMatch_FallsBackToResolutionAndScaling_WhenBoundsShift()
    {
        // Simulate Windows re-laying out virtual coords after plug/unplug.
        var prev = new ScreenMatchCandidate(
            PlatformDisplayName: "",
            Bounds: new PixelRect(0, 0, 2560, 1440),
            Scaling: 1.25,
            IsPrimary: false);

        var a = C("A", -2560, 0, 2560, 1440, scaling: 1.25);
        var b = C("B", 0, 0, 1920, 1080, scaling: 1.0, primary: true);

        var match = ScreenMatching.FindBestMatch(prev, new[] { a, b });
        Assert.NotNull(match);
        Assert.Equal("A", match!.Name);
    }

    [Fact]
    public void FindBestMatch_ChoosesNearestByCenter_WhenAmbiguous()
    {
        var prev = new ScreenMatchCandidate(
            PlatformDisplayName: "",
            Bounds: new PixelRect(1920, 0, 1920, 1080),
            Scaling: 1.0,
            IsPrimary: false);

        var left = C("Left", 0, 0, 1920, 1080);
        var right = C("Right", 1920, 0, 1920, 1080);
        var far = C("Far", 0, 2000, 1920, 1080);

        var match = ScreenMatching.FindBestMatch(prev, new[] { left, right, far });
        Assert.NotNull(match);
        Assert.Equal("Right", match!.Name);
    }

    [Fact]
    public void FindBestMatch_WhenPreviousWasPrimary_PrefersCurrentPrimary()
    {
        var prev = new ScreenMatchCandidate(
            PlatformDisplayName: "",
            Bounds: new PixelRect(0, 0, 1920, 1080),
            Scaling: 1.0,
            IsPrimary: true);

        var primary = C("Primary", 100, 100, 1920, 1080, primary: true);
        var other = C("Other", 2100, 0, 1920, 1080);

        var match = ScreenMatching.FindBestMatch(prev, new[] { other, primary });
        Assert.NotNull(match);
        Assert.Equal("Primary", match!.Name);
    }
}
