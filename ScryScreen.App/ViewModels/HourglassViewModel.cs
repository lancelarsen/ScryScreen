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

    // Grid-sand simulation tuning
    private const double MinGravity = 1.0;
    private const double MaxGravity = 400.0;
    private const double MinDensity = 0.0;
    private const double MaxDensity = 10.0;
    private const double MinParticleSize = 2.0;
    private const double MaxParticleSize = 14.0;
    private const int MinMaxRelease = 1;
    private const int MaxMaxRelease = 5000;

    private const int MinParticleCount = 50;
    private const int MaxParticleCount = 8000;

    private HourglassPhysicsSettings _physics = HourglassPhysicsSettings.Default;

    public HourglassViewModel()
    {
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(200), DispatcherPriority.Background, (_, _) => Tick());
        DurationMinutesText = "1";
        DurationSecondsText = "0";
        OverlayOpacity = 0.80;

        // Physics defaults
        ParticleCountText = HourglassPhysicsSettings.Default.ParticleCount.ToString();
        GravityText = HourglassPhysicsSettings.Default.Gravity.ToString("0");
        DensityText = HourglassPhysicsSettings.Default.Density.ToString("0.00");
        ParticleSizeText = HourglassPhysicsSettings.Default.ParticleSize.ToString("0.0");
        MaxReleasePerFrameText = HourglassPhysicsSettings.Default.MaxReleasePerFrame.ToString();

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

    [ObservableProperty]
    private string particleCountText = "1000";

    [ObservableProperty]
    private string gravityText = "2600";

    [ObservableProperty]
    private string densityText = "5.00";

    [ObservableProperty]
    private string particleSizeText = "6.0";

    [ObservableProperty]
    private string maxReleasePerFrameText = "12";

    [ObservableProperty]
    private bool showSandPhysics;


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

    public string DurationText
        => FormatClockLike(GetDuration());

    public string RemainingOverDurationText
        => $"{RemainingText} / {DurationText}";

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

    partial void OnRemainingChanged(TimeSpan value)
    {
        OnPropertyChanged(nameof(FractionRemaining));
        OnPropertyChanged(nameof(RemainingText));
        OnPropertyChanged(nameof(RemainingOverDurationText));
    }

    partial void OnOverlayOpacityChanged(double value)
    {
        if (OverlayOpacity < 0) OverlayOpacity = 0;
        if (OverlayOpacity > 1) OverlayOpacity = 1;
        StateChanged?.Invoke();
    }

    partial void OnGravityTextChanged(string value)
    {
        TryUpdatePhysicsFromText();
        StateChanged?.Invoke();
    }

    partial void OnDensityTextChanged(string value)
    {
        TryUpdatePhysicsFromText();
        StateChanged?.Invoke();
    }

    partial void OnParticleSizeTextChanged(string value)
    {
        TryUpdatePhysicsFromText();
        StateChanged?.Invoke();
    }

    partial void OnParticleCountTextChanged(string value)
    {
        TryUpdatePhysicsFromText();
        StateChanged?.Invoke();
    }

    partial void OnMaxReleasePerFrameTextChanged(string value)
    {
        TryUpdatePhysicsFromText();
        StateChanged?.Invoke();
    }

    private void NotifyTimeChanged()
    {
        // If the user edits the duration while not running, treat it like setting up a new countdown.
        // This keeps Remaining and the sand fraction consistent without starting motion.
        if (!IsRunning)
        {
            Remaining = GetDuration();
        }

        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(FractionRemaining));
        OnPropertyChanged(nameof(RemainingText));
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(RemainingOverDurationText));

        NotifyCommandStates();
        StateChanged?.Invoke();
    }

    partial void OnIsRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanStop));
        NotifyCommandStates();
        StateChanged?.Invoke();
    }

    private void NotifyCommandStates()
    {
        // Avalonia may disable command sources based on ICommand.CanExecute even if IsEnabled is also bound.
        StartCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
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

    private void TryUpdatePhysicsFromText()
    {
        // With UpdateSourceTrigger=PropertyChanged, clamping/rewriting text while typing is hostile.
        // Keep the user's text and update a cached, clamped numeric settings struct when parsing succeeds.

        _physics = _physics with { ParticleCount = Clamp(ParseInt(ParticleCountText), MinParticleCount, MaxParticleCount) };

        if (TryParseDoubleLoose(GravityText, out var gravity))
        {
            _physics = _physics with { Gravity = Clamp(gravity, MinGravity, MaxGravity) };
        }

        if (TryParseDoubleLoose(DensityText, out var density))
        {
            _physics = _physics with { Density = Clamp(density, MinDensity, MaxDensity) };
        }

        if (TryParseDoubleLoose(ParticleSizeText, out var particleSize))
        {
            _physics = _physics with { ParticleSize = Clamp(particleSize, MinParticleSize, MaxParticleSize) };
        }

        _physics = _physics with { MaxReleasePerFrame = Clamp(ParseInt(MaxReleasePerFrameText), MinMaxRelease, MaxMaxRelease) };
    }

    private HourglassPhysicsSettings GetPhysicsSettings()
    {
        TryUpdatePhysicsFromText();
        return _physics;
    }

    public HourglassState SnapshotState()
        => new(GetDuration(), Remaining, IsRunning, GetPhysicsSettings());

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
        OnPropertyChanged(nameof(RemainingOverDurationText));
        StateChanged?.Invoke();
    }

    private static string FormatClockLike(TimeSpan time)
    {
        if (time < TimeSpan.Zero)
        {
            time = TimeSpan.Zero;
        }

        var totalSeconds = (int)Math.Ceiling(time.TotalSeconds);
        if (totalSeconds < 0) totalSeconds = 0;

        var minutes = totalSeconds / 60;
        var seconds = totalSeconds % 60;
        return minutes > 0
            ? $"{minutes}:{seconds:00}"
            : $"0:{seconds:00}";
    }

    private void StopInternal()
    {
        IsRunning = false;
        _timer.Stop();
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanStop));

        NotifyCommandStates();
    }

    private static int ParseInt(string? text)
        => int.TryParse(text, out var v) ? v : 0;

    private static bool TryParseDoubleLoose(string? text, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        // Accept both "0.5" and "0,5" regardless of OS locale.
        var normalized = text.Trim().Replace(',', '.');
        return double.TryParse(
            normalized,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out value);
    }

    private static int Clamp(int value, int min, int max)
        => value < min ? min : (value > max ? max : value);

    private static double Clamp(double value, double min, double max)
        => value < min ? min : (value > max ? max : value);

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

        NotifyCommandStates();
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
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(RemainingOverDurationText));
        StateChanged?.Invoke();
    }

    [RelayCommand]
    private void ResetPhysics()
    {
        var d = HourglassPhysicsSettings.Default;
        ParticleCountText = d.ParticleCount.ToString();
        GravityText = d.Gravity.ToString("0");
        DensityText = d.Density.ToString("0.00");
        ParticleSizeText = d.ParticleSize.ToString("0.0");
        MaxReleasePerFrameText = d.MaxReleasePerFrame.ToString();

        _physics = d;
        TryUpdatePhysicsFromText();
        StateChanged?.Invoke();
    }
}
