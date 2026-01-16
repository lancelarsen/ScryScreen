using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScryScreen.App.Models;

namespace ScryScreen.App.ViewModels;

public partial class MapMasterViewModel : ViewModelBase
{
    public event Action? StateChanged;

    public MapMasterViewModel()
    {
        var d = MapMasterState.Default;
        OverlayOpacity = d.OverlayOpacity;
        RevealX = d.RevealX;
        RevealY = d.RevealY;
        RevealWidth = d.RevealWidth;
        RevealHeight = d.RevealHeight;
        NormalizeReveal();
    }

    [ObservableProperty]
    private double overlayOpacity;

    [ObservableProperty]
    private double revealX;

    [ObservableProperty]
    private double revealY;

    [ObservableProperty]
    private double revealWidth;

    [ObservableProperty]
    private double revealHeight;

    public MapMasterState SnapshotState()
        => new(
            OverlayOpacity: OverlayOpacity,
            RevealX: RevealX,
            RevealY: RevealY,
            RevealWidth: RevealWidth,
            RevealHeight: RevealHeight);

    [RelayCommand]
    private void ResetRevealToDefault()
    {
        var d = MapMasterState.Default;
        OverlayOpacity = d.OverlayOpacity;
        RevealX = d.RevealX;
        RevealY = d.RevealY;
        RevealWidth = d.RevealWidth;
        RevealHeight = d.RevealHeight;
        NormalizeReveal();
        StateChanged?.Invoke();
    }

    partial void OnOverlayOpacityChanged(double value)
    {
        if (OverlayOpacity < 0) OverlayOpacity = 0;
        if (OverlayOpacity > 1) OverlayOpacity = 1;
        StateChanged?.Invoke();
    }

    partial void OnRevealXChanged(double value)
    {
        NormalizeReveal();
        StateChanged?.Invoke();
    }

    partial void OnRevealYChanged(double value)
    {
        NormalizeReveal();
        StateChanged?.Invoke();
    }

    partial void OnRevealWidthChanged(double value)
    {
        NormalizeReveal();
        StateChanged?.Invoke();
    }

    partial void OnRevealHeightChanged(double value)
    {
        NormalizeReveal();
        StateChanged?.Invoke();
    }

    private void NormalizeReveal()
    {
        // Clamp sizes first.
        RevealWidth = Clamp(RevealWidth, 0.02, 1.0);
        RevealHeight = Clamp(RevealHeight, 0.02, 1.0);

        // Clamp position into available space.
        RevealX = Clamp(RevealX, 0.0, Math.Max(0.0, 1.0 - RevealWidth));
        RevealY = Clamp(RevealY, 0.0, Math.Max(0.0, 1.0 - RevealHeight));
    }

    private static double Clamp(double value, double min, double max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }
}
