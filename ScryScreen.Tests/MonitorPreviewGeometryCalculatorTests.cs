using ScryScreen.App.Services;
using ScryScreen.App.ViewModels;
using Xunit;

namespace ScryScreen.Tests;

public class MonitorPreviewGeometryCalculatorTests
{
    [Fact]
    public void TryCalculate_ReturnsFalse_WhenInputsInvalid()
    {
        Assert.False(MonitorPreviewGeometryCalculator.TryCalculate(
            outerWidth: 120,
            outerHeight: 60,
            monitorWidthPx: 0,
            monitorHeightPx: 1080,
            mediaWidthPx: 800,
            mediaHeightPx: 600,
            scaleMode: MediaScaleMode.FillHeight,
            align: MediaAlign.Center,
            out _));

        Assert.False(MonitorPreviewGeometryCalculator.TryCalculate(
            outerWidth: 120,
            outerHeight: 60,
            monitorWidthPx: 1920,
            monitorHeightPx: 1080,
            mediaWidthPx: 0,
            mediaHeightPx: 600,
            scaleMode: MediaScaleMode.FillHeight,
            align: MediaAlign.Center,
            out _));

        Assert.False(MonitorPreviewGeometryCalculator.TryCalculate(
            outerWidth: 0,
            outerHeight: 60,
            monitorWidthPx: 1920,
            monitorHeightPx: 1080,
            mediaWidthPx: 800,
            mediaHeightPx: 600,
            scaleMode: MediaScaleMode.FillHeight,
            align: MediaAlign.Center,
            out _));
    }

    [Fact]
    public void FillHeight_CentersHorizontally_ForNarrowerMedia()
    {
        // 16:9 monitor preview inside 120x60.
        // Use 4:3 media which should letterbox horizontally with FillHeight.
        var ok = MonitorPreviewGeometryCalculator.TryCalculate(
            outerWidth: 120,
            outerHeight: 60,
            monitorWidthPx: 1920,
            monitorHeightPx: 1080,
            mediaWidthPx: 800,
            mediaHeightPx: 600,
            scaleMode: MediaScaleMode.FillHeight,
            align: MediaAlign.Center,
            out var g);

        Assert.True(ok);
        Assert.Equal(60, g.ImageHeight);
        Assert.Equal(80, g.ImageWidth);

        // Screen is ~106.66 wide, so leftover is ~26.66, center => ~13.33 => floor 13.
        Assert.Equal(13, g.ImageLeft);
        Assert.Equal(0, g.ImageTop);
    }

    [Fact]
    public void FillHeight_AlignStart_ShiftsLeft()
    {
        var ok = MonitorPreviewGeometryCalculator.TryCalculate(
            outerWidth: 120,
            outerHeight: 60,
            monitorWidthPx: 1920,
            monitorHeightPx: 1080,
            mediaWidthPx: 800,
            mediaHeightPx: 600,
            scaleMode: MediaScaleMode.FillHeight,
            align: MediaAlign.Start,
            out var g);

        Assert.True(ok);
        Assert.Equal(0, g.ImageLeft);
        Assert.Equal(0, g.ImageTop);
    }

    [Fact]
    public void FillHeight_AlignEnd_ShiftsRight()
    {
        var ok = MonitorPreviewGeometryCalculator.TryCalculate(
            outerWidth: 120,
            outerHeight: 60,
            monitorWidthPx: 1920,
            monitorHeightPx: 1080,
            mediaWidthPx: 800,
            mediaHeightPx: 600,
            scaleMode: MediaScaleMode.FillHeight,
            align: MediaAlign.End,
            out var g);

        Assert.True(ok);

        // leftover ~26.66 => floor 26
        Assert.Equal(26, g.ImageLeft);
        Assert.Equal(0, g.ImageTop);
    }

    [Fact]
    public void FillWidth_AlignEnd_ShiftsDown()
    {
        // Portrait monitor preview inside 120x60.
        // With FillWidth, 4:3 media should become short vertically, leaving positive leftoverY.
        var ok = MonitorPreviewGeometryCalculator.TryCalculate(
            outerWidth: 120,
            outerHeight: 60,
            monitorWidthPx: 1080,
            monitorHeightPx: 1920,
            mediaWidthPx: 800,
            mediaHeightPx: 600,
            scaleMode: MediaScaleMode.FillWidth,
            align: MediaAlign.End,
            out var g);

        Assert.True(ok);

        // In this setup screenW is 33.75 and screenH is 60.
        // FillWidth => displayW == screenW (ceil => 34), displayH ~25.3125 (ceil => 26)
        Assert.Equal(34, g.ImageWidth);
        Assert.Equal(26, g.ImageHeight);

        // leftoverY ~34.6875 => floor 34
        Assert.Equal(0, g.ImageLeft);
        Assert.Equal(34, g.ImageTop);
    }

    [Fact]
    public void MatchingAspectRatios_DoNotProduceNegativeOnePixelOffsets()
    {
        // When media aspect ratio matches monitor aspect ratio, leftovers should clamp to 0
        // even if floating-point math produces tiny epsilon values.
        var ok = MonitorPreviewGeometryCalculator.TryCalculate(
            outerWidth: 120,
            outerHeight: 60,
            monitorWidthPx: 1920,
            monitorHeightPx: 1080,
            mediaWidthPx: 1920,
            mediaHeightPx: 1080,
            scaleMode: MediaScaleMode.FillHeight,
            align: MediaAlign.Center,
            out var g);

        Assert.True(ok);
        Assert.Equal(0, g.ImageLeft);
        Assert.Equal(0, g.ImageTop);
    }
}
