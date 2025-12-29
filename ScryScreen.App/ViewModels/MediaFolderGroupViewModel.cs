using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace ScryScreen.App.ViewModels;

public sealed class MediaFolderGroupViewModel : ViewModelBase
{
    public MediaFolderGroupViewModel(string header)
    {
        Header = header;
        Items.CollectionChanged += OnItemsCollectionChanged;
    }

    public string Header { get; }

    public string DisplayHeader => $"{Header} ({Items.Count})";

    public ObservableCollection<MediaItemViewModel> Items { get; } = new();

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(DisplayHeader));
    }
}
