using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ScryScreen.App.ViewModels;

public sealed partial class MediaLibraryViewModel : ViewModelBase
{
    public ObservableCollection<MediaItemViewModel> Items { get; } = new();

    public ObservableCollection<MediaFolderGroupViewModel> Groups { get; } = new();

    public MediaLibraryViewModel()
    {
        Items.CollectionChanged += OnItemsCollectionChanged;
    }

    public int ImagesCount => Items.Count;

    public bool HasAnyMedia => Items.Count > 0;

    public bool HasNoMedia => Items.Count == 0;

    public string ImagesHeader => Items.Count > 0
        ? $"Images ({Items.Count})"
        : "Images";

    public string MediaTabHeader => Items.Count > 0
        ? $"Media ({Items.Count})"
        : "Media";

    [RelayCommand]
    private void SelectItem(MediaItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        SelectedItem = item;
    }

    [ObservableProperty]
    private MediaItemViewModel? selectedItem;

    [ObservableProperty]
    private string statusText = "No media imported";

    [ObservableProperty]
    private bool isGroupedView = false;

    public bool IsFlatView
    {
        get => !IsGroupedView;
        set => IsGroupedView = !value;
    }

    partial void OnIsGroupedViewChanged(bool value)
    {
        OnPropertyChanged(nameof(IsFlatView));
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(ImagesCount));
        OnPropertyChanged(nameof(ImagesHeader));
        OnPropertyChanged(nameof(MediaTabHeader));
        OnPropertyChanged(nameof(HasAnyMedia));
        OnPropertyChanged(nameof(HasNoMedia));
    }

    public void ImportFolder(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            StatusText = "Folder not found";
            return;
        }

        var supportedImages = new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };
        var supportedVideos = new[] { ".mp4" };

        foreach (var item in Items)
        {
            item.Thumbnail?.Dispose();
        }
        Items.Clear();
        Groups.Clear();

        // Root group is the selected folder name (e.g., "dragons").
        var rootName = Path.GetFileName(Path.TrimEndingDirectorySeparator(folderPath));
        if (string.IsNullOrWhiteSpace(rootName))
        {
            rootName = folderPath;
        }

        void AddGroupForDirectory(string directoryPath)
        {
            var relative = Path.GetRelativePath(folderPath, directoryPath);
            var header = relative == "."
                ? rootName
                : $"{rootName} / {relative.Replace("\\", " / ").Replace("/", " / ")}";

            var group = new MediaFolderGroupViewModel(header);

            var localFiles = Directory
                .EnumerateFiles(directoryPath, "*.*", SearchOption.TopDirectoryOnly)
                .Where(f =>
                {
                    var ext = Path.GetExtension(f);
                    return supportedImages.Contains(ext, StringComparer.OrdinalIgnoreCase) ||
                           supportedVideos.Contains(ext, StringComparer.OrdinalIgnoreCase);
                })
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

            foreach (var file in localFiles)
            {
                Bitmap? thumb = null;
                var ext = Path.GetExtension(file);
                var isVideo = supportedVideos.Contains(ext, StringComparer.OrdinalIgnoreCase);

                try
                {
                    if (!isVideo)
                    {
                        using var stream = File.OpenRead(file);
                        thumb = new Bitmap(stream);
                    }
                }
                catch
                {
                    // ignore unreadable files
                }

                var vm = new MediaItemViewModel(file, thumb, isVideo: isVideo);
                Items.Add(vm);
                group.Items.Add(vm);
            }

            // Only show groups that have images, OR the root group (so you at least see the folder name).
            if (group.Items.Count > 0 || relative == ".")
            {
                Groups.Add(group);
            }

            foreach (var subDir in Directory.EnumerateDirectories(directoryPath).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                AddGroupForDirectory(subDir);
            }
        }

        AddGroupForDirectory(folderPath);

        StatusText = Items.Count == 0
            ? "No supported images found"
            : $"{Items.Count} image(s)";

        OnPropertyChanged(nameof(ImagesCount));
        OnPropertyChanged(nameof(ImagesHeader));
        OnPropertyChanged(nameof(MediaTabHeader));
        OnPropertyChanged(nameof(HasAnyMedia));
        OnPropertyChanged(nameof(HasNoMedia));

        SelectedItem = Items.FirstOrDefault();
    }
}
