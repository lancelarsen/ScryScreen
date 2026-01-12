using System;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScryScreen.App.Models;

namespace ScryScreen.App.ViewModels;

public partial class HourglassViewModel : ViewModelBase
{
    private readonly DispatcherTimer _timer;
    private DateTime _lastTickUtc;

    public event Action? StateChanged;

    public HourglassViewModel()
    {
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(200), DispatcherPriority.Background, (_, _) => Tick());
        DurationMinutesText = "1";
        DurationSecondsText = "0";
        OverlayOpacity = 0.85;
        Reset();
    }

    [ObservableProperty]
    private string durationMinutesText = "0";

    [ObservableProperty]
    private string durationSecondsText = "0";

    [ObservableProperty]
    private double overlayOpacity;

    [ObservableProperty]
    private bool isRunning;

    [ObservableProperty]
    private TimeSpan remaining;

    public double FractionRemaining
    {
        get
        {
            var d = GetDuration().TotalMilliseconds;
            if (d <= 0) return 0;
            var r = Remaining.TotalMilliseconds;
            if (r <= 0) return 0;
            if (r >= d) return 1;
            return r / d;
        }
    }

    public string RemainingText
    {
        get
        {
            var t = Remaining;
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

    public bool CanStart => !IsRunning && GetDuration() > TimeSpan.Zero;

    public bool CanStop => IsRunning;

    partial void OnDurationMinutesTextChanged(string value)
    {
        ClampDurationFields();
        NotifyTimeChanged();
    }

    partial void OnDurationSecondsTextChanged(string value)
    {
        ClampDurationFields();
        NotifyTimeChanged();
    }

    partial void OnOverlayOpacityChanged(double value)
    {
        if (OverlayOpacity < 0) OverlayOpacity = 0;
        if (OverlayOpacity > 1) OverlayOpacity = 1;
        StateChanged?.Invoke();
    }

    private void NotifyTimeChanged()
    {
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(FractionRemaining));
        OnPropertyChanged(nameof(RemainingText));
        StateChanged?.Invoke();
    }

    private void ClampDurationFields()
    {
        var m = ParseInt(DurationMinutesText);
        var s = ParseInt(DurationSecondsText);

        if (m < 0) m = 0;
        if (m > 999) m = 999;

        if (s < 0) s = 0;
        if (s > 59) s = 59;

        var newM = m.ToString();
        var newS = s.ToString();

        if (!string.Equals(DurationMinutesText, newM, StringComparison.Ordinal))
        {
            DurationMinutesText = newM;
        }

        if (!string.Equals(DurationSecondsText, newS, StringComparison.Ordinal))
        {
            DurationSecondsText = newS;
        }
    }

    private TimeSpan GetDuration()
    {
        var m = ParseInt(DurationMinutesText);
        var s = ParseInt(DurationSecondsText);
        if (m < 0) m = 0;
        if (s < 0) s = 0;
        if (s > 59) s = 59;

        var total = (m * 60) + s;
        if (total < 0) total = 0;
        return TimeSpan.FromSeconds(total);
    }

    public HourglassState SnapshotState()
        => new(GetDuration(), Remaining, IsRunning);

    private void Tick()
    {
        if (!IsRunning)
        {
            _timer.Stop();
            return;
        }

        var now = DateTime.UtcNow;
        var dt = now - _lastTickUtc;
        _lastTickUtc = now;

        // Guard against clock jumps.
        if (dt < TimeSpan.Zero || dt > TimeSpan.FromSeconds(1))
        {
            dt = TimeSpan.FromMilliseconds(200);
        }

        Remaining -= dt;

        if (Remaining <= TimeSpan.Zero)
        {
            Remaining = TimeSpan.Zero;
            StopInternal();
            // Caller can interpret Remaining==0 && !IsRunning as timeout.
        }

        OnPropertyChanged(nameof(FractionRemaining));
        OnPropertyChanged(nameof(RemainingText));
        StateChanged?.Invoke();
    }

    private void StopInternal()
    {
        IsRunning = false;
        _timer.Stop();
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanStop));
    }

    private static int ParseInt(string? text)
        => int.TryParse(text, out var v) ? v : 0;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Start()
    {
        if (IsRunning)
        {
            return;
        }

        var duration = GetDuration();
        if (duration <= TimeSpan.Zero)
        {
            return;
        }

        if (Remaining <= TimeSpan.Zero || Remaining > duration)
        {
            Remaining = duration;
        }

        IsRunning = true;
        _lastTickUtc = DateTime.UtcNow;
        _timer.Start();

        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanStop));
        StateChanged?.Invoke();
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop()
    {
        if (!IsRunning)
        {
            return;
        }

        StopInternal();
        StateChanged?.Invoke();
    }

    [RelayCommand]
    private void Reset()
    {
        StopInternal();
        Remaining = GetDuration();
        OnPropertyChanged(nameof(FractionRemaining));
        OnPropertyChanged(nameof(RemainingText));
        StateChanged?.Invoke();
    }
}
