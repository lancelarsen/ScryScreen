using System;
using System.IO;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ScryScreen.App.ViewModels;

public sealed partial class MediaItemViewModel : ViewModelBase
{
    private (int Width, int Height)? _pixelSize;
    private readonly long? _fileSizeBytes;

    private bool _normalizingEffectRanges;

    public MediaItemViewModel(string filePath, Bitmap? thumbnail, bool isVideo = false)
    {
        FilePath = filePath;
        Thumbnail = thumbnail;
        IsVideo = isVideo;

        try
        {
            _fileSizeBytes = new FileInfo(FilePath).Length;
        }
        catch
        {
            _fileSizeBytes = null;
        }

        _pixelSize = TryGetPixelSizeFromThumbnail(thumbnail);
    }

    public string FilePath { get; }

    public string DisplayName => System.IO.Path.GetFileName(FilePath);

    public bool IsVideo { get; }

    public bool IsImage => !IsVideo;

    public bool HasPixelSize => !string.IsNullOrWhiteSpace(PixelSizeText);

    public bool HasFileSize => !string.IsNullOrWhiteSpace(FileSizeText);

    public string HoverToolTip
    {
        get
        {
            var size = PixelSizeText;
            var bytes = FileSizeText;

            if (string.IsNullOrWhiteSpace(size) && string.IsNullOrWhiteSpace(bytes))
            {
                return DisplayName;
            }

            if (string.IsNullOrWhiteSpace(size))
            {
                return $"{DisplayName}\n{bytes}";
            }

            if (string.IsNullOrWhiteSpace(bytes))
            {
                return $"{DisplayName}\n{size}";
            }

            return $"{DisplayName}\n{size}\n{bytes}";
        }
    }

    public string PixelSizeText
    {
        get
        {
            if (_pixelSize is null)
            {
                _pixelSize = TryGetPixelSizeFromFile(FilePath);
            }

            return _pixelSize is null
                ? string.Empty
                : $"{_pixelSize.Value.Width}×{_pixelSize.Value.Height}";
        }
    }

    public string FileSizeText
        => _fileSizeBytes is null ? string.Empty : FormatBytes(_fileSizeBytes.Value);

    [ObservableProperty]
    private Bitmap? thumbnail;

    // Overlay effects (per-media). Multiple can be enabled at once.
    [ObservableProperty]
    private bool rainEnabled;

    [ObservableProperty]
    private bool rainSoundEnabled;

    [ObservableProperty]
    private double rainMin = 0.1;

    [ObservableProperty]
    private double rainMax = 5;

    [ObservableProperty]
    private double rainIntensity = 0.5;

    [ObservableProperty]
    private bool snowEnabled;

    [ObservableProperty]
    private bool snowSoundEnabled;

    [ObservableProperty]
    private double snowMin = 0.1;

    [ObservableProperty]
    private double snowMax = 4;

    [ObservableProperty]
    private double snowIntensity = 0.5;

    [ObservableProperty]
    private bool ashEnabled;

    [ObservableProperty]
    private bool ashSoundEnabled;

    [ObservableProperty]
    private double ashMin = 0.1;

    [ObservableProperty]
    private double ashMax = 4;

    [ObservableProperty]
    private double ashIntensity = 0.5;

    [ObservableProperty]
    private bool fireEnabled;

    [ObservableProperty]
    private bool fireSoundEnabled;

    [ObservableProperty]
    private double fireMin = 1;

    [ObservableProperty]
    private double fireMax = 5;

    [ObservableProperty]
    private double fireIntensity = 1;

    [ObservableProperty]
    private bool sandEnabled;

    [ObservableProperty]
    private bool sandSoundEnabled;

    [ObservableProperty]
    private double sandMin = 0.1;

    [ObservableProperty]
    private double sandMax = 5;

    [ObservableProperty]
    private double sandIntensity = 0.5;

    [ObservableProperty]
    private bool fogEnabled;

    [ObservableProperty]
    private bool fogSoundEnabled;

    [ObservableProperty]
    private double fogMin = 0.5;

    [ObservableProperty]
    private double fogMax = 4;

    [ObservableProperty]
    private double fogIntensity = 1;

    [ObservableProperty]
    private bool smokeEnabled;

    [ObservableProperty]
    private bool smokeSoundEnabled;

    [ObservableProperty]
    private double smokeMin = 0.5;

    [ObservableProperty]
    private double smokeMax = 2;

    [ObservableProperty]
    private double smokeIntensity = 0.5;

    [ObservableProperty]
    private bool lightningEnabled;

    [ObservableProperty]
    private bool lightningSoundEnabled;

    [ObservableProperty]
    private double lightningMin = 0.1;

    [ObservableProperty]
    private double lightningMax = 5;

    [ObservableProperty]
    private double lightningIntensity = 0.35;

    [ObservableProperty]
    private bool quakeEnabled;

    [ObservableProperty]
    private bool quakeSoundEnabled;

    [ObservableProperty]
    private double quakeMin = 0.1;

    [ObservableProperty]
    private double quakeMax = 5;

    [ObservableProperty]
    private double quakeIntensity = 0.35;

    private static double SanitizeNonNegative(double v)
    {
        if (double.IsNaN(v) || double.IsInfinity(v))
        {
            return 0;
        }

        return v < 0 ? 0 : v;
    }

    private void NormalizeRange(
        Func<double> getMin,
        Action<double> setMin,
        Func<double> getMax,
        Action<double> setMax,
        Func<double> getValue,
        Action<double> setValue)
    {
        if (_normalizingEffectRanges)
        {
            return;
        }

        _normalizingEffectRanges = true;
        try
        {
            var min = SanitizeNonNegative(getMin());
            var max = SanitizeNonNegative(getMax());

            if (max < min)
            {
                max = min;
            }

            if (min != getMin())
            {
                setMin(min);
            }

            if (max != getMax())
            {
                setMax(max);
            }

            var v = getValue();
            if (double.IsNaN(v) || double.IsInfinity(v))
            {
                v = min;
            }

            if (v < min)
            {
                setValue(min);
            }
            else if (v > max)
            {
                setValue(max);
            }
        }
        finally
        {
            _normalizingEffectRanges = false;
        }
    }

    partial void OnRainMinChanged(double value) => NormalizeRange(() => RainMin, v => RainMin = v, () => RainMax, v => RainMax = v, () => RainIntensity, v => RainIntensity = v);
    partial void OnRainMaxChanged(double value) => NormalizeRange(() => RainMin, v => RainMin = v, () => RainMax, v => RainMax = v, () => RainIntensity, v => RainIntensity = v);

    partial void OnSnowMinChanged(double value) => NormalizeRange(() => SnowMin, v => SnowMin = v, () => SnowMax, v => SnowMax = v, () => SnowIntensity, v => SnowIntensity = v);
    partial void OnSnowMaxChanged(double value) => NormalizeRange(() => SnowMin, v => SnowMin = v, () => SnowMax, v => SnowMax = v, () => SnowIntensity, v => SnowIntensity = v);

    partial void OnAshMinChanged(double value) => NormalizeRange(() => AshMin, v => AshMin = v, () => AshMax, v => AshMax = v, () => AshIntensity, v => AshIntensity = v);
    partial void OnAshMaxChanged(double value) => NormalizeRange(() => AshMin, v => AshMin = v, () => AshMax, v => AshMax = v, () => AshIntensity, v => AshIntensity = v);

    partial void OnFireMinChanged(double value) => NormalizeRange(() => FireMin, v => FireMin = v, () => FireMax, v => FireMax = v, () => FireIntensity, v => FireIntensity = v);
    partial void OnFireMaxChanged(double value) => NormalizeRange(() => FireMin, v => FireMin = v, () => FireMax, v => FireMax = v, () => FireIntensity, v => FireIntensity = v);

    partial void OnSandMinChanged(double value) => NormalizeRange(() => SandMin, v => SandMin = v, () => SandMax, v => SandMax = v, () => SandIntensity, v => SandIntensity = v);
    partial void OnSandMaxChanged(double value) => NormalizeRange(() => SandMin, v => SandMin = v, () => SandMax, v => SandMax = v, () => SandIntensity, v => SandIntensity = v);

    partial void OnFogMinChanged(double value) => NormalizeRange(() => FogMin, v => FogMin = v, () => FogMax, v => FogMax = v, () => FogIntensity, v => FogIntensity = v);
    partial void OnFogMaxChanged(double value) => NormalizeRange(() => FogMin, v => FogMin = v, () => FogMax, v => FogMax = v, () => FogIntensity, v => FogIntensity = v);

    partial void OnSmokeMinChanged(double value) => NormalizeRange(() => SmokeMin, v => SmokeMin = v, () => SmokeMax, v => SmokeMax = v, () => SmokeIntensity, v => SmokeIntensity = v);
    partial void OnSmokeMaxChanged(double value) => NormalizeRange(() => SmokeMin, v => SmokeMin = v, () => SmokeMax, v => SmokeMax = v, () => SmokeIntensity, v => SmokeIntensity = v);

    partial void OnLightningMinChanged(double value) => NormalizeRange(() => LightningMin, v => LightningMin = v, () => LightningMax, v => LightningMax = v, () => LightningIntensity, v => LightningIntensity = v);
    partial void OnLightningMaxChanged(double value) => NormalizeRange(() => LightningMin, v => LightningMin = v, () => LightningMax, v => LightningMax = v, () => LightningIntensity, v => LightningIntensity = v);

    partial void OnQuakeMinChanged(double value) => NormalizeRange(() => QuakeMin, v => QuakeMin = v, () => QuakeMax, v => QuakeMax = v, () => QuakeIntensity, v => QuakeIntensity = v);
    partial void OnQuakeMaxChanged(double value) => NormalizeRange(() => QuakeMin, v => QuakeMin = v, () => QuakeMax, v => QuakeMax = v, () => QuakeIntensity, v => QuakeIntensity = v);

    partial void OnThumbnailChanged(Bitmap? value)
    {
        _pixelSize = TryGetPixelSizeFromThumbnail(value) ?? _pixelSize;
        OnPropertyChanged(nameof(HoverToolTip));
        OnPropertyChanged(nameof(PixelSizeText));
        OnPropertyChanged(nameof(HasPixelSize));
    }

    public void SetPixelSize(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        _pixelSize = (width, height);
        OnPropertyChanged(nameof(HoverToolTip));
        OnPropertyChanged(nameof(PixelSizeText));
        OnPropertyChanged(nameof(HasPixelSize));
    }

    private static (int Width, int Height)? TryGetPixelSizeFromThumbnail(Bitmap? bitmap)
    {
        if (bitmap is null)
        {
            return null;
        }

        try
        {
            return (bitmap.PixelSize.Width, bitmap.PixelSize.Height);
        }
        catch
        {
            return null;
        }
    }

    private static (int Width, int Height)? TryGetPixelSizeFromFile(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            using var bitmap = new Bitmap(stream);
            return (bitmap.PixelSize.Width, bitmap.PixelSize.Height);
        }
        catch
        {
            return null;
        }
    }

    private static string FormatBytes(long bytes)
    {
        var abs = Math.Abs((double)bytes);
        string[] units = ["B", "KB", "MB", "GB", "TB"];

        var unitIndex = 0;
        while (abs >= 1024 && unitIndex < units.Length - 1)
        {
            abs /= 1024;
            unitIndex++;
        }

        if (unitIndex == 0)
        {
            return $"{bytes} {units[unitIndex]}";
        }

        return $"{abs:0.0} {units[unitIndex]}";
    }
}
