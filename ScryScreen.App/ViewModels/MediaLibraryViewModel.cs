using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NAudio.Wave;
using ScryScreen.App.Services;
using ScryScreen.Core.Utilities;

namespace ScryScreen.App.ViewModels;

public sealed partial class MediaLibraryViewModel : ViewModelBase
{
    private int _importGeneration;

    private readonly ObservableCollection<MediaItemViewModel> _allItems = new();
    private readonly ObservableCollection<MediaFolderGroupViewModel> _allGroups = new();

    public IReadOnlyList<MediaItemViewModel> AllItems => _allItems;

    public ObservableCollection<MediaItemViewModel> Items { get; } = new();

    public ObservableCollection<MediaFolderGroupViewModel> Groups { get; } = new();

    public MediaLibraryViewModel()
    {
        Items.CollectionChanged += OnItemsCollectionChanged;
    }

    public enum MediaCategory
    {
        Images,
        Video,
        Audio,
    }

    [ObservableProperty]
    private MediaCategory selectedCategory = MediaCategory.Images;

    partial void OnSelectedCategoryChanged(MediaCategory value)
    {
        RebuildFilteredCollections(value);
        OnPropertyChanged(nameof(ImagesCount));
        OnPropertyChanged(nameof(VideoCount));
        OnPropertyChanged(nameof(AudioCount));
        OnPropertyChanged(nameof(ImagesTabHeader));
        OnPropertyChanged(nameof(VideoTabHeader));
        OnPropertyChanged(nameof(AudioTabHeader));
        OnPropertyChanged(nameof(HasAnyMedia));
        OnPropertyChanged(nameof(HasNoMedia));
        OnPropertyChanged(nameof(HasAnyItemsInSelectedCategory));
        OnPropertyChanged(nameof(HasNoItemsInSelectedCategory));
    }

    public int ImagesCount => _allItems.Count(i => i.IsImage);

    public int VideoCount => _allItems.Count(i => i.IsVideo);

    public int AudioCount => _allItems.Count(i => i.IsAudio);

    public bool HasAnyMedia => _allItems.Count > 0;

    public bool HasNoMedia => _allItems.Count == 0;

    public bool HasAnyItemsInSelectedCategory => Items.Count > 0;

    public bool HasNoItemsInSelectedCategory => Items.Count == 0;

    public string ImagesTabHeader => ImagesCount > 0 ? $"Images ({ImagesCount})" : "Images";

    public string VideoTabHeader => VideoCount > 0 ? $"Video ({VideoCount})" : "Video";

    public string AudioTabHeader => AudioCount > 0 ? $"Audio ({AudioCount})" : "Audio";

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
        // Kept for backward compatibility if anything still listens to Items.
        OnPropertyChanged(nameof(ImagesCount));
        OnPropertyChanged(nameof(VideoCount));
        OnPropertyChanged(nameof(AudioCount));
        OnPropertyChanged(nameof(ImagesTabHeader));
        OnPropertyChanged(nameof(VideoTabHeader));
        OnPropertyChanged(nameof(AudioTabHeader));
        OnPropertyChanged(nameof(HasAnyMedia));
        OnPropertyChanged(nameof(HasNoMedia));
        OnPropertyChanged(nameof(HasAnyItemsInSelectedCategory));
        OnPropertyChanged(nameof(HasNoItemsInSelectedCategory));
    }

    public void ImportFolder(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            StatusText = "Folder not found";
            return;
        }

        _importGeneration++;
        var importGeneration = _importGeneration;

        foreach (var item in _allItems)
        {
            item.Thumbnail?.Dispose();
        }
        _allItems.Clear();
        _allGroups.Clear();
        Items.Clear();
        Groups.Clear();

        // Root group is the selected folder name (e.g., "dragons").
        var rootName = Path.GetFileName(Path.TrimEndingDirectorySeparator(folderPath));
        if (string.IsNullOrWhiteSpace(rootName))
        {
            rootName = folderPath;
        }

        static bool IsIgnoredDirectory(string directoryPath)
        {
            var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(directoryPath));
            return string.Equals(name, "ignore", StringComparison.OrdinalIgnoreCase);
        }

        void AddGroupForDirectory(string directoryPath)
        {
            if (IsIgnoredDirectory(directoryPath))
            {
                return;
            }

            var relative = Path.GetRelativePath(folderPath, directoryPath);
            var header = relative == "."
                ? rootName
                : $"{rootName} / {relative.Replace("\\", " / ").Replace("/", " / ")}";

            var group = new MediaFolderGroupViewModel(header);

            var localFiles = Directory
                .EnumerateFiles(directoryPath, "*.*", SearchOption.TopDirectoryOnly)
                .Where(f =>
                {
                    return MediaFileClassifier.IsImage(f) || MediaFileClassifier.IsVideo(f) || MediaFileClassifier.IsAudio(f);
                })
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

            foreach (var file in localFiles)
            {
                Bitmap? thumb = null;
                var isVideo = MediaFileClassifier.IsVideo(file);
                var isAudio = MediaFileClassifier.IsAudio(file);
                var audioDuration = TimeSpan.Zero;

                if (isAudio)
                {
                    try
                    {
                        using var reader = new AudioFileReader(file);
                        audioDuration = reader.TotalTime;
                    }
                    catch
                    {
                        audioDuration = TimeSpan.Zero;
                    }
                }

                try
                {
                    if (!isVideo && !isAudio)
                    {
                        using var stream = File.OpenRead(file);
                        thumb = new Bitmap(stream);
                    }
                }
                catch
                {
                    // ignore unreadable files
                }

                var vm = new MediaItemViewModel(file, thumb, isVideo: isVideo, isAudio: isAudio)
                {
                    AudioDuration = audioDuration,
                };
                _allItems.Add(vm);
                group.Items.Add(vm);

                if (isVideo)
                {
                    _ = PopulateVideoThumbnailAsync(vm, file, importGeneration);
                }
            }

            // Only show groups that have images, OR the root group (so you at least see the folder name).
            if (group.Items.Count > 0 || relative == ".")
            {
                _allGroups.Add(group);
            }

            foreach (var subDir in Directory.EnumerateDirectories(directoryPath).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                if (IsIgnoredDirectory(subDir))
                {
                    continue;
                }

                AddGroupForDirectory(subDir);
            }
        }

        AddGroupForDirectory(folderPath);

        RebuildFilteredCollections(SelectedCategory);

        StatusText = _allItems.Count == 0
            ? "No supported media found"
            : $"{_allItems.Count} item(s)";

        OnPropertyChanged(nameof(ImagesCount));
        OnPropertyChanged(nameof(VideoCount));
        OnPropertyChanged(nameof(AudioCount));
        OnPropertyChanged(nameof(ImagesTabHeader));
        OnPropertyChanged(nameof(VideoTabHeader));
        OnPropertyChanged(nameof(AudioTabHeader));
        OnPropertyChanged(nameof(HasAnyMedia));
        OnPropertyChanged(nameof(HasNoMedia));

        SelectedItem = Items.FirstOrDefault();
    }

    private void RebuildFilteredCollections(MediaCategory category)
    {
        bool Matches(MediaItemViewModel item) => category switch
        {
            MediaCategory.Images => item.IsImage,
            MediaCategory.Video => item.IsVideo,
            MediaCategory.Audio => item.IsAudio,
            _ => true,
        };

        Items.Clear();
        foreach (var item in _allItems.Where(Matches))
        {
            Items.Add(item);
        }

        Groups.Clear();
        foreach (var g in _allGroups)
        {
            var fg = new MediaFolderGroupViewModel(g.Header);
            foreach (var item in g.Items.Where(Matches))
            {
                fg.Items.Add(item);
            }

            if (fg.Items.Count > 0)
            {
                Groups.Add(fg);
            }
        }

        if (SelectedItem is not null && !Items.Contains(SelectedItem))
        {
            SelectedItem = Items.FirstOrDefault();
        }

        OnPropertyChanged(nameof(HasAnyItemsInSelectedCategory));
        OnPropertyChanged(nameof(HasNoItemsInSelectedCategory));
    }

    private async Task PopulateVideoThumbnailAsync(MediaItemViewModel item, string filePath, int importGeneration)
    {
        if (importGeneration != _importGeneration)
        {
            return;
        }

        var snap = await VideoSnapshotService.CaptureFirstFrameAsync(filePath, maxWidth: 512, maxHeight: 288).ConfigureAwait(false);
        if (snap is null)
        {
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (importGeneration != _importGeneration ||
                !string.Equals(item.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
            {
                snap.Bitmap.Dispose();
                return;
            }

            item.Thumbnail?.Dispose();
            item.Thumbnail = snap.Bitmap;
            item.SetPixelSize(snap.VideoWidth, snap.VideoHeight);
        });
    }
}
