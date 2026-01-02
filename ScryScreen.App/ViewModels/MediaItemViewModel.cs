using System;
using System.IO;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ScryScreen.App.ViewModels;

public sealed partial class MediaItemViewModel : ViewModelBase
{
    private (int Width, int Height)? _pixelSize;
    private readonly long? _fileSizeBytes;

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
    private double rainIntensity = 0.5;

    [ObservableProperty]
    private bool snowEnabled;

    [ObservableProperty]
    private double snowIntensity = 0.5;

    [ObservableProperty]
    private bool fogEnabled;

    [ObservableProperty]
    private double fogIntensity = 0.5;

    [ObservableProperty]
    private bool smokeEnabled;

    [ObservableProperty]
    private double smokeIntensity = 0.5;

    [ObservableProperty]
    private bool lightningEnabled;

    [ObservableProperty]
    private double lightningIntensity = 0.35;

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
