using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScryScreen.App.Services;

namespace ScryScreen.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly PortalHostService _portalHost;
    private readonly ReadOnlyCollection<ScreenInfoViewModel> _screens;

    private sealed record PortalContentSnapshot(bool IsVisible, string CurrentAssignment, string? AssignedMediaFilePath);
    private readonly System.Collections.Generic.Dictionary<int, System.Collections.Generic.Stack<PortalContentSnapshot>> _portalHistory = new();

    public MainWindowViewModel(PortalHostService portalHost)
    {
        _portalHost = portalHost ?? throw new ArgumentNullException(nameof(portalHost));
        _screens = new ReadOnlyCollection<ScreenInfoViewModel>(_portalHost.GetScreens().ToList());
        Portals = new ObservableCollection<PortalRowViewModel>();
        Media = new MediaLibraryViewModel();

        Media.PropertyChanged += OnMediaPropertyChanged;

        _portalHost.PortalClosed += OnPortalClosed;

        // Start with one portal for convenience.
        AddPortal();
    }

    public ReadOnlyCollection<ScreenInfoViewModel> Screens => _screens;

    public ObservableCollection<PortalRowViewModel> Portals { get; }

    public MediaLibraryViewModel Media { get; }

    [ObservableProperty]
    private bool isPortalsSectionVisible = true;

    [ObservableProperty]
    private bool isLibrarySectionVisible = true;

    [ObservableProperty]
    private bool isControlsSectionVisible = false;

    [ObservableProperty]
    private bool isAlwaysOnTop = true;

    private void OnMediaPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MediaLibraryViewModel.SelectedItem))
        {
            UpdatePortalMediaSelectionFlags();
        }
    }

    [RelayCommand]
    private void AddPortal()
    {
        var portalNumber = Portals.Count + 1;
        var defaultScreen = Screens.FirstOrDefault();

        _portalHost.CreatePortal(portalNumber, defaultScreen);

        var portalRow = new PortalRowViewModel(_portalHost, portalNumber, Screens)
        {
            SelectedScreen = defaultScreen,
        };

        portalRow.DeleteRequested += OnDeletePortalRequested;

        Portals.Add(portalRow);

        UpdatePortalMediaSelectionFlags();
    }

    public void ImportMediaFolder(string folderPath)
    {
        Media.ImportFolder(folderPath);
        UpdatePortalMediaSelectionFlags();
    }

    [RelayCommand]
    private void ToggleSelectedMediaForPortal(PortalRowViewModel? portal)
    {
        if (portal is null)
        {
            return;
        }

        // NOTE: IsSelectedForCurrentMedia is TwoWay-bound to the ToggleButton.
        // At this point it already reflects the user's click (checked/unchecked).
        if (portal.IsSelectedForCurrentMedia)
        {
            var item = Media.SelectedItem;
            if (item is null)
            {
                // No selected media: revert the toggle back off.
                portal.IsSelectedForCurrentMedia = false;
                return;
            }

            PushSnapshot(portal);
            ApplyMediaToPortal(portal, item.DisplayName, item.FilePath);
        }
        else
        {
            RestorePreviousContent(portal);
        }

        UpdatePortalMediaSelectionFlags();
    }

    private void PushSnapshot(PortalRowViewModel portal)
    {
        if (!_portalHistory.TryGetValue(portal.PortalNumber, out var stack))
        {
            stack = new System.Collections.Generic.Stack<PortalContentSnapshot>();
            _portalHistory[portal.PortalNumber] = stack;
        }

        stack.Push(new PortalContentSnapshot(
            IsVisible: portal.IsVisible,
            CurrentAssignment: portal.CurrentAssignment,
            AssignedMediaFilePath: portal.AssignedMediaFilePath));
    }

    private void ApplyMediaToPortal(PortalRowViewModel portal, string displayName, string filePath)
    {
        portal.CurrentAssignment = displayName;
        portal.AssignedMediaFilePath = filePath;
        portal.SetAssignedPreviewFromFile(filePath);
        _portalHost.SetContentImage(portal.PortalNumber, filePath, displayName);
        portal.IsVisible = true;
    }

    private void RestorePreviousContent(PortalRowViewModel portal)
    {
        if (!_portalHistory.TryGetValue(portal.PortalNumber, out var stack) || stack.Count == 0)
        {
            // Fallback: restore to idle.
            portal.AssignedMediaFilePath = null;
            portal.CurrentAssignment = "Idle";
            portal.AssignedPreview = null;
            _portalHost.SetContentText(portal.PortalNumber, portal.CurrentAssignment);
            return;
        }

        var snapshot = stack.Pop();

        portal.IsVisible = snapshot.IsVisible;
        portal.CurrentAssignment = snapshot.CurrentAssignment;
        portal.AssignedMediaFilePath = snapshot.AssignedMediaFilePath;

        if (!string.IsNullOrWhiteSpace(snapshot.AssignedMediaFilePath))
        {
            portal.SetAssignedPreviewFromFile(snapshot.AssignedMediaFilePath);
            _portalHost.SetContentImage(portal.PortalNumber, snapshot.AssignedMediaFilePath, snapshot.CurrentAssignment);
        }
        else
        {
            portal.AssignedPreview = null;
            _portalHost.SetContentText(portal.PortalNumber, snapshot.CurrentAssignment);
        }

        _portalHost.SetVisibility(portal.PortalNumber, snapshot.IsVisible);
    }

    private void UpdatePortalMediaSelectionFlags()
    {
        var selectedPath = Media.SelectedItem?.FilePath;
        foreach (var portal in Portals)
        {
            portal.IsSelectedForCurrentMedia =
                !string.IsNullOrWhiteSpace(selectedPath) &&
                string.Equals(portal.AssignedMediaFilePath, selectedPath, StringComparison.OrdinalIgnoreCase);
        }
    }

    [RelayCommand]
    private Task IdentifyPortalsAsync()
    {
        return _portalHost.IdentifyAllAsync();
    }

    [RelayCommand]
    private void ClearPortalContent(PortalRowViewModel? portal)
    {
        if (portal is null)
        {
            return;
        }

        _portalHistory.Remove(portal.PortalNumber);

        portal.AssignedMediaFilePath = null;
        portal.CurrentAssignment = "Idle";
        portal.AssignedPreview = null;
        portal.IsSelectedForCurrentMedia = false;

        _portalHost.ClearContent(portal.PortalNumber);
        UpdatePortalMediaSelectionFlags();
    }

    public void Shutdown()
    {
        _portalHost.CloseAll();
    }

    private void OnDeletePortalRequested(int portalNumber)
    {
        _portalHost.ClosePortal(portalNumber);
    }

    private void OnPortalClosed(int portalNumber)
    {
        var row = Portals.FirstOrDefault(p => p.PortalNumber == portalNumber);
        if (row is null)
        {
            return;
        }

        row.DeleteRequested -= OnDeletePortalRequested;
        Portals.Remove(row);

        UpdatePortalMediaSelectionFlags();
    }
}
