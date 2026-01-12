using System;
using CommunityToolkit.Mvvm.ComponentModel;
using ScryScreen.App.Models;

namespace ScryScreen.App.ViewModels;

public partial class HourglassPortalViewModel : ViewModelBase
{
    public HourglassPortalViewModel(HourglassState state)
    {
        Update(state);
    }

    [ObservableProperty]
    private double overlayOpacity = 1.0;

    [ObservableProperty]
    private double fractionRemaining;

    [ObservableProperty]
    private string remainingText = "";

    [ObservableProperty]
    private bool isRunning;

    public void Update(HourglassState state)
    {
        FractionRemaining = state.FractionRemaining;
        RemainingText = Format(state.Remaining);
        IsRunning = state.IsRunning;
    }

    private static string Format(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        var totalSeconds = (int)Math.Ceiling(t.TotalSeconds);
        if (totalSeconds < 0) totalSeconds = 0;

        var minutes = totalSeconds / 60;
        var seconds = totalSeconds % 60;
        return minutes > 0
            ? $"{minutes}:{seconds:00}"
            : $"0:{seconds:00}";
    }
}
