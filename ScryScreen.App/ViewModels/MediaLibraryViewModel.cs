using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ScryScreen.App.ViewModels;

public sealed partial class MediaLibraryViewModel : ViewModelBase
{
    public ObservableCollection<MediaItemViewModel> Items { get; } = new();

    public MediaLibraryViewModel()
    {
        Items.CollectionChanged += OnItemsCollectionChanged;
    }

    public int ImagesCount => Items.Count;

    public string ImagesHeader => Items.Count > 0
        ? $"Images ({Items.Count})"
        : "Images";

    [ObservableProperty]
    private MediaItemViewModel? selectedItem;

    [ObservableProperty]
    private string statusText = "No media imported";

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(ImagesCount));
        OnPropertyChanged(nameof(ImagesHeader));
    }

    public void ImportFolder(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            StatusText = "Folder not found";
            return;
        }

        var supported = new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };
        var files = Directory
            .EnumerateFiles(folderPath, "*.*", SearchOption.AllDirectories)
            .Where(f => supported.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var item in Items)
        {
            item.Thumbnail?.Dispose();
        }
        Items.Clear();
        foreach (var file in files)
        {
            Bitmap? thumb = null;
            try
            {
                using var stream = File.OpenRead(file);
                thumb = new Bitmap(stream);
            }
            catch
            {
                // ignore unreadable files
            }

            Items.Add(new MediaItemViewModel(file, thumb));
        }

        StatusText = files.Count == 0
            ? "No supported images found"
            : $"{files.Count} image(s)";

        OnPropertyChanged(nameof(ImagesCount));
        OnPropertyChanged(nameof(ImagesHeader));

        SelectedItem = Items.FirstOrDefault();
    }
}
