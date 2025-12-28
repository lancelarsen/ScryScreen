using System;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ScryScreen.App.ViewModels;

public sealed partial class MediaItemViewModel : ViewModelBase
{
    public MediaItemViewModel(string filePath, Bitmap? thumbnail)
    {
        FilePath = filePath;
        Thumbnail = thumbnail;
    }

    public string FilePath { get; }

    public string DisplayName => System.IO.Path.GetFileName(FilePath);

    [ObservableProperty]
    private Bitmap? thumbnail;
}
