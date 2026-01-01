using System;
using ScryScreen.App.ViewModels;

namespace ScryScreen.App.Services;

public static class MonitorPreviewGeometryCalculator
{
    public readonly record struct Geometry(
        double ScreenWidth,
        double ScreenHeight,
        double ImageLeft,
        double ImageTop,
        double ImageWidth,
        double ImageHeight);

    public static bool TryCalculate(
        double outerWidth,
        double outerHeight,
        int monitorWidthPx,
        int monitorHeightPx,
        int mediaWidthPx,
        int mediaHeightPx,
        MediaScaleMode scaleMode,
        MediaAlign align,
        out Geometry geometry)
    {
        geometry = default;

        if (outerWidth <= 0 || outerHeight <= 0)
        {
            return false;
        }

        if (monitorWidthPx <= 0 || monitorHeightPx <= 0)
        {
            return false;
        }

        if (mediaWidthPx <= 0 || mediaHeightPx <= 0)
        {
            return false;
        }

        // Scale the actual monitor aspect ratio to fit the fixed preview box.
        var monitorScale = Math.Min(outerWidth / monitorWidthPx, outerHeight / monitorHeightPx);
        var screenW = monitorWidthPx * monitorScale;
        var screenH = monitorHeightPx * monitorScale;

        var sx = screenW / mediaWidthPx;
        var sy = screenH / mediaHeightPx;

        // FillHeight => displayed height matches monitor preview height.
        // FillWidth  => displayed width  matches monitor preview width.
        var displayW = 0.0;
        var displayH = 0.0;

        switch (scaleMode)
        {
            case MediaScaleMode.FillHeight:
                displayH = screenH;
                displayW = mediaWidthPx * sy;
                break;
            case MediaScaleMode.FillWidth:
                displayW = screenW;
                displayH = mediaHeightPx * sx;
                break;
            default:
                displayW = screenW;
                displayH = mediaHeightPx * sx;
                break;
        }

        var leftoverX = screenW - displayW;
        var leftoverY = screenH - displayH;

        // Clamp tiny floating-point leftovers to 0.
        if (Math.Abs(leftoverX) < 1e-6)
        {
            leftoverX = 0;
        }

        if (Math.Abs(leftoverY) < 1e-6)
        {
            leftoverY = 0;
        }

        var ax = align switch
        {
            MediaAlign.Start => 0.0,
            MediaAlign.Center => 0.5,
            MediaAlign.End => 1.0,
            _ => 0.5,
        };

        // Align only on the axis that might overflow/letterbox for the selected scale mode.
        // FillHeight => horizontal align; FillWidth => vertical align.
        var left = scaleMode == MediaScaleMode.FillHeight ? leftoverX * ax : leftoverX * 0.5;
        var top = scaleMode == MediaScaleMode.FillWidth ? leftoverY * ax : leftoverY * 0.5;

        // Snap to pixel-ish boundaries for the preview.
        var snappedW = Math.Ceiling(displayW);
        var snappedH = Math.Ceiling(displayH);
        var snappedLeft = Math.Floor(left);
        var snappedTop = Math.Floor(top);

        geometry = new Geometry(
            ScreenWidth: screenW,
            ScreenHeight: screenH,
            ImageLeft: snappedLeft,
            ImageTop: snappedTop,
            ImageWidth: snappedW,
            ImageHeight: snappedH);

        return true;
    }
}
