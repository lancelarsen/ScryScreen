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
    private string? _lastSelectedMediaPath;

    private sealed record PortalContentSnapshot(bool IsVisible, string CurrentAssignment, string? AssignedMediaFilePath, MediaScaleMode ScaleMode, MediaAlign Align);
    private readonly System.Collections.Generic.Dictionary<int, System.Collections.Generic.Stack<PortalContentSnapshot>> _portalHistory = new();

    public MainWindowViewModel(PortalHostService portalHost)
    {
        _portalHost = portalHost ?? throw new ArgumentNullException(nameof(portalHost));
        _screens = new ReadOnlyCollection<ScreenInfoViewModel>(_portalHost.GetScreens().ToList());
        Portals = new ObservableCollection<PortalRowViewModel>();
        Media = new MediaLibraryViewModel();

        SelectedScaleMode = MediaScaleMode.FillHeight;
        SelectedAlign = MediaAlign.Center;

        Media.PropertyChanged += OnMediaPropertyChanged;

        _portalHost.PortalClosed += OnPortalClosed;

        // Start with one portal for convenience.
        AddPortal();
    }

    public ReadOnlyCollection<ScreenInfoViewModel> Screens => _screens;

    public ObservableCollection<PortalRowViewModel> Portals { get; }

    public MediaLibraryViewModel Media { get; }

    [ObservableProperty]
    private MediaScaleMode selectedScaleMode;

    [ObservableProperty]
    private MediaAlign selectedAlign;

    public bool IsFillHeight => SelectedScaleMode == MediaScaleMode.FillHeight;

    public bool IsFillWidth => SelectedScaleMode == MediaScaleMode.FillWidth;

    public bool IsAlignStart => SelectedAlign == MediaAlign.Start;

    public bool IsAlignCenter => SelectedAlign == MediaAlign.Center;

    public bool IsAlignEnd => SelectedAlign == MediaAlign.End;

    partial void OnSelectedScaleModeChanged(MediaScaleMode value)
    {
        OnPropertyChanged(nameof(IsFillHeight));
        OnPropertyChanged(nameof(IsFillWidth));
        OnPropertyChanged(nameof(IsAlignStart));
        OnPropertyChanged(nameof(IsAlignCenter));
        OnPropertyChanged(nameof(IsAlignEnd));

        // Reset to a sane default for the new axis.
        SelectedAlign = MediaAlign.Center;
        ApplyDisplayOptionsToAssignedPortals();
    }

    partial void OnSelectedAlignChanged(MediaAlign value)
    {
        OnPropertyChanged(nameof(IsAlignStart));
        OnPropertyChanged(nameof(IsAlignCenter));
        OnPropertyChanged(nameof(IsAlignEnd));
        ApplyDisplayOptionsToAssignedPortals();
    }

    [RelayCommand]
    private void SetScaleFillHeight()
    {
        SelectedScaleMode = MediaScaleMode.FillHeight;
    }

    [RelayCommand]
    private void SetScaleFillWidth()
    {
        SelectedScaleMode = MediaScaleMode.FillWidth;
    }

    [RelayCommand]
    private void SetAlignStart()
    {
        SelectedAlign = MediaAlign.Start;
    }

    [RelayCommand]
    private void SetAlignCenter()
    {
        SelectedAlign = MediaAlign.Center;
    }

    [RelayCommand]
    private void SetAlignEnd()
    {
        SelectedAlign = MediaAlign.End;
    }

    private void ApplyDisplayOptionsToAssignedPortals()
    {
        foreach (var portal in Portals)
        {
            if (string.IsNullOrWhiteSpace(portal.AssignedMediaFilePath))
            {
                continue;
            }

            portal.ScaleMode = SelectedScaleMode;
            portal.Align = SelectedAlign;
            _portalHost.SetDisplayOptions(portal.PortalNumber, portal.ScaleMode, portal.Align);
        }
    }

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

            var selectedPath = Media.SelectedItem?.FilePath;
            if (_lastSelectedMediaPath is null && selectedPath is not null && !IsControlsSectionVisible)
            {
                IsControlsSectionVisible = true;
            }

            _lastSelectedMediaPath = selectedPath;
        }
    }

    [RelayCommand]
    private void SelectMediaItem(MediaItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        var current = Media.SelectedItem;
        var isAlreadySelected = ReferenceEquals(current, item) ||
                                (current is not null &&
                                 string.Equals(current.FilePath, item.FilePath, StringComparison.OrdinalIgnoreCase));

        Media.SelectedItem = item;

        if (!isAlreadySelected && !IsControlsSectionVisible)
        {
            IsControlsSectionVisible = true;
        }
    }

    [RelayCommand]
    private void AddPortal()
    {
        var portalNumber = GetNextAvailablePortalNumber();
        var defaultScreen = Screens.FirstOrDefault();

        _portalHost.CreatePortal(portalNumber, defaultScreen);

        var portalRow = new PortalRowViewModel(_portalHost, portalNumber, Screens)
        {
            SelectedScreen = defaultScreen,
            ScaleMode = SelectedScaleMode,
            Align = SelectedAlign,
        };

        portalRow.DeleteRequested += OnDeletePortalRequested;

        Portals.Add(portalRow);

        _portalHost.SetDisplayOptions(portalNumber, portalRow.ScaleMode, portalRow.Align);

        UpdatePortalMediaSelectionFlags();
    }

    private int GetNextAvailablePortalNumber()
    {
        // Reuse the lowest available number so add/remove doesn't collide.
        var used = new System.Collections.Generic.HashSet<int>(Portals.Select(p => p.PortalNumber));
        var candidate = 1;
        while (used.Contains(candidate))
        {
            candidate++;
        }

        return candidate;
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
            AssignedMediaFilePath: portal.AssignedMediaFilePath,
            ScaleMode: portal.ScaleMode,
            Align: portal.Align));
    }

    private void ApplyMediaToPortal(PortalRowViewModel portal, string displayName, string filePath)
    {
        portal.CurrentAssignment = displayName;
        portal.AssignedMediaFilePath = filePath;
        portal.SetAssignedPreviewFromFile(filePath);
        portal.ScaleMode = SelectedScaleMode;
        portal.Align = SelectedAlign;
        _portalHost.SetContentImage(portal.PortalNumber, filePath, displayName, portal.ScaleMode, portal.Align);
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
        portal.ScaleMode = snapshot.ScaleMode;
        portal.Align = snapshot.Align;

        if (!string.IsNullOrWhiteSpace(snapshot.AssignedMediaFilePath))
        {
            portal.SetAssignedPreviewFromFile(snapshot.AssignedMediaFilePath);
            _portalHost.SetContentImage(portal.PortalNumber, snapshot.AssignedMediaFilePath, snapshot.CurrentAssignment, portal.ScaleMode, portal.Align);
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

        _portalHistory.Remove(portalNumber);

        row.DeleteRequested -= OnDeletePortalRequested;
        Portals.Remove(row);

        UpdatePortalMediaSelectionFlags();
    }
}
