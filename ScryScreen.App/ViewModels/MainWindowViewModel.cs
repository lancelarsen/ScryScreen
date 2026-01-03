using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScryScreen.App.Models;
using ScryScreen.App.Services;
using ScryScreen.Core.Utilities;

namespace ScryScreen.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly PortalHostService _portalHost;
    private readonly ReadOnlyCollection<ScreenInfoViewModel> _screens;
    private string? _lastSelectedMediaPath;

    private readonly InitiativeTrackerViewModel _initiativeTracker;

    private MediaItemViewModel? _selectedMediaForEffects;

    private readonly System.Collections.Generic.Dictionary<int, System.Collections.Generic.Stack<PortalContentRestorationPlanner.Snapshot>> _portalHistory = new();

    public MainWindowViewModel(PortalHostService portalHost)
    {
        _portalHost = portalHost ?? throw new ArgumentNullException(nameof(portalHost));
        _screens = new ReadOnlyCollection<ScreenInfoViewModel>(_portalHost.GetScreens().ToList());
        Portals = new ObservableCollection<PortalRowViewModel>();
        Media = new MediaLibraryViewModel();

        _initiativeTracker = new InitiativeTrackerViewModel();
        _initiativeTracker.StateChanged += OnInitiativeStateChanged;

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

    public InitiativeTrackerViewModel InitiativeTracker => _initiativeTracker;

    [RelayCommand]
    private void ToggleInitiativeForPortal(PortalRowViewModel? portal)
    {
        if (portal is null)
        {
            return;
        }

        // Don't rely on TwoWay binding timing (ToggleButton may execute the command
        // before the VM property updates). Treat this command as the single source of truth.
        var shouldSelect = !portal.IsSelectedForInitiative;
        portal.IsSelectedForInitiative = shouldSelect;

        if (shouldSelect)
        {
            PushSnapshot(portal);
            ApplyInitiativeToPortal(portal);
        }
        else
        {
            RestorePreviousContent(portal);
        }
    }

    [ObservableProperty]
    private bool isVideoLoop = false;

    public bool IsSelectedMediaVideo => Media.SelectedItem?.IsVideo == true;

    partial void OnIsVideoLoopChanged(bool value) => ApplyVideoOptionsToAssignedPortals();

    public enum LibraryTab
    {
        Media,
        Apps,
    }

    [ObservableProperty]
    private LibraryTab selectedLibraryTab = LibraryTab.Media;

    public bool IsMediaTabSelected => SelectedLibraryTab == LibraryTab.Media;

    public bool IsAppsTabSelected => SelectedLibraryTab == LibraryTab.Apps;

    partial void OnSelectedLibraryTabChanged(LibraryTab value)
    {
        OnPropertyChanged(nameof(IsMediaTabSelected));
        OnPropertyChanged(nameof(IsAppsTabSelected));

        if (value == LibraryTab.Apps && !IsControlsSectionVisible)
        {
            IsControlsSectionVisible = true;
        }
    }

    [RelayCommand]
    private void ShowMediaTab() => SelectedLibraryTab = LibraryTab.Media;

    [RelayCommand]
    private void ShowAppsTab() => SelectedLibraryTab = LibraryTab.Apps;

    [RelayCommand]
    private void SelectInitiativeTrackerApp()
    {
        SelectedLibraryTab = LibraryTab.Apps;

        if (!IsControlsSectionVisible)
        {
            IsControlsSectionVisible = true;
        }
    }

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
    private bool isAlwaysOnTop = false;

    [ObservableProperty]
    private bool showEffectSliderBounds = false;

    private void OnMediaPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MediaLibraryViewModel.SelectedItem))
        {
            UpdatePortalMediaSelectionFlags();
            OnPropertyChanged(nameof(IsSelectedMediaVideo));

            AttachSelectedMediaEffects(Media.SelectedItem);
            ApplyEffectsToAssignedPortals();

            var selectedPath = Media.SelectedItem?.FilePath;
            if (_lastSelectedMediaPath is null && selectedPath is not null && !IsControlsSectionVisible)
            {
                IsControlsSectionVisible = true;
            }

            _lastSelectedMediaPath = selectedPath;
        }
    }

    private void AttachSelectedMediaEffects(MediaItemViewModel? item)
    {
        if (_selectedMediaForEffects is not null)
        {
            _selectedMediaForEffects.PropertyChanged -= OnSelectedMediaForEffectsChanged;
        }

        _selectedMediaForEffects = item;

        if (_selectedMediaForEffects is not null)
        {
            _selectedMediaForEffects.PropertyChanged += OnSelectedMediaForEffectsChanged;
        }
    }

    private void OnSelectedMediaForEffectsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MediaItemViewModel.RainEnabled) or nameof(MediaItemViewModel.RainIntensity) or nameof(MediaItemViewModel.RainMin) or nameof(MediaItemViewModel.RainMax) or
            nameof(MediaItemViewModel.SnowEnabled) or nameof(MediaItemViewModel.SnowIntensity) or nameof(MediaItemViewModel.SnowMin) or nameof(MediaItemViewModel.SnowMax) or
            nameof(MediaItemViewModel.AshEnabled) or nameof(MediaItemViewModel.AshIntensity) or nameof(MediaItemViewModel.AshMin) or nameof(MediaItemViewModel.AshMax) or
            nameof(MediaItemViewModel.FireEnabled) or nameof(MediaItemViewModel.FireIntensity) or nameof(MediaItemViewModel.FireMin) or nameof(MediaItemViewModel.FireMax) or
            nameof(MediaItemViewModel.SandEnabled) or nameof(MediaItemViewModel.SandIntensity) or nameof(MediaItemViewModel.SandMin) or nameof(MediaItemViewModel.SandMax) or
            nameof(MediaItemViewModel.FogEnabled) or nameof(MediaItemViewModel.FogIntensity) or nameof(MediaItemViewModel.FogMin) or nameof(MediaItemViewModel.FogMax) or
            nameof(MediaItemViewModel.SmokeEnabled) or nameof(MediaItemViewModel.SmokeIntensity) or nameof(MediaItemViewModel.SmokeMin) or nameof(MediaItemViewModel.SmokeMax) or
            nameof(MediaItemViewModel.LightningEnabled) or nameof(MediaItemViewModel.LightningIntensity) or nameof(MediaItemViewModel.LightningMin) or nameof(MediaItemViewModel.LightningMax) or
            nameof(MediaItemViewModel.QuakeEnabled) or nameof(MediaItemViewModel.QuakeIntensity) or nameof(MediaItemViewModel.QuakeMin) or nameof(MediaItemViewModel.QuakeMax))
        {
            ApplyEffectsToAssignedPortals();
        }
    }

    private long _lightningTriggerNonce;
    private long _quakeTriggerNonce;

    private OverlayEffectsState BuildEffectsState(MediaItemViewModel item)
    {
        static double ClampMin0(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v))
            {
                return 0;
            }

            return v < 0 ? 0 : v;
        }

        return new OverlayEffectsState(
            RainEnabled: item.RainEnabled,
            RainIntensity: ClampMin0(item.RainIntensity),
            SnowEnabled: item.SnowEnabled,
            SnowIntensity: ClampMin0(item.SnowIntensity),
            AshEnabled: item.AshEnabled,
            AshIntensity: ClampMin0(item.AshIntensity),
            FireEnabled: item.FireEnabled,
            FireIntensity: ClampMin0(item.FireIntensity),
            SandEnabled: item.SandEnabled,
            SandIntensity: ClampMin0(item.SandIntensity),
            FogEnabled: item.FogEnabled,
            FogIntensity: ClampMin0(item.FogIntensity),
            SmokeEnabled: item.SmokeEnabled,
            SmokeIntensity: ClampMin0(item.SmokeIntensity),
            LightningEnabled: item.LightningEnabled,
            LightningIntensity: ClampMin0(item.LightningIntensity),
            QuakeEnabled: item.QuakeEnabled,
            QuakeIntensity: ClampMin0(item.QuakeIntensity),
                LightningTrigger: _lightningTriggerNonce,
                QuakeTrigger: _quakeTriggerNonce);
    }

    private void ApplyEffectsToAssignedPortals()
    {
        var selected = Media.SelectedItem;
        if (selected is null)
        {
            return;
        }

        var selectedPath = selected.FilePath;
        var effects = BuildEffectsState(selected);

        foreach (var portal in Portals)
        {
            if (string.Equals(portal.AssignedMediaFilePath, selectedPath, StringComparison.OrdinalIgnoreCase))
            {
                _portalHost.SetOverlayEffects(portal.PortalNumber, effects);
                portal.OverlayEffects = effects;
            }
        }
    }

    [RelayCommand]
    private void TriggerLightning()
    {
        var selected = Media.SelectedItem;
        if (selected is null)
        {
            return;
        }

        if (!selected.LightningEnabled)
        {
            selected.LightningEnabled = true;
        }

        if (selected.LightningIntensity < selected.LightningMin)
        {
            selected.LightningIntensity = selected.LightningMin;
        }

        _lightningTriggerNonce++;
        ApplyEffectsToAssignedPortals();
    }

    [RelayCommand]
    private void TriggerQuake()
    {
        var selected = Media.SelectedItem;
        if (selected is null)
        {
            return;
        }

        if (!selected.QuakeEnabled)
        {
            selected.QuakeEnabled = true;
        }

        if (selected.QuakeIntensity < selected.QuakeMin)
        {
            selected.QuakeIntensity = selected.QuakeMin;
        }

        _quakeTriggerNonce++;
        ApplyEffectsToAssignedPortals();
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
        // Startup UX: if multiple monitors exist, put the first portal on a non-primary monitor.
        // Subsequent portals keep the existing default ordering.
        var defaultIndex = DefaultScreenSelector.GetDefaultScreenIndex(
            isPrimaryByIndex: Screens.Select(s => s.IsPrimary).ToList(),
            isFirstPortal: Portals.Count == 0);

        var defaultScreen = (defaultIndex >= 0 && defaultIndex < Screens.Count)
            ? Screens[defaultIndex]
            : null;

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
        return PortalNumberAllocator.GetNextAvailable(Portals.Select(p => p.PortalNumber));
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
            stack = new System.Collections.Generic.Stack<PortalContentRestorationPlanner.Snapshot>();
            _portalHistory[portal.PortalNumber] = stack;
        }

        stack.Push(new PortalContentRestorationPlanner.Snapshot(
            IsVisible: portal.IsVisible,
            CurrentAssignment: portal.CurrentAssignment,
            AssignedMediaFilePath: portal.AssignedMediaFilePath,
            IsVideo: portal.IsVideoAssigned,
            IsVideoLoop: portal.IsVideoLoop,
            ScaleMode: portal.ScaleMode,
            Align: portal.Align));
    }

    private void ApplyMediaToPortal(PortalRowViewModel portal, string displayName, string filePath)
    {
        portal.IsSelectedForInitiative = false;

        portal.CurrentAssignment = displayName;
        portal.AssignedMediaFilePath = filePath;
        portal.ScaleMode = SelectedScaleMode;
        portal.Align = SelectedAlign;

        var selected = Media.SelectedItem;
        var effects = selected is null ? OverlayEffectsState.None : BuildEffectsState(selected);
        if (selected?.IsVideo == true)
        {
            portal.AssignedPreview = null;
            portal.IsVideoLoop = IsVideoLoop;
            _portalHost.SetContentVideo(portal.PortalNumber, filePath, displayName, portal.ScaleMode, portal.Align, loop: IsVideoLoop);
            _portalHost.SetOverlayEffects(portal.PortalNumber, effects);
            portal.OverlayEffects = effects;
            portal.IsVideoPlaying = false;

            // Start preview/paused state at ~1s to avoid blank first-frame.
            _portalHost.SeekVideo(portal.PortalNumber, 1000);

            _ = UpdatePortalVideoSnapshotAsync(portal, filePath);
        }
        else
        {
            portal.SetAssignedPreviewFromFile(filePath);
            _portalHost.SetContentImage(portal.PortalNumber, filePath, displayName, portal.ScaleMode, portal.Align);
            _portalHost.SetOverlayEffects(portal.PortalNumber, effects);
            portal.OverlayEffects = effects;
            portal.IsVideoPlaying = false;
            portal.IsVideoLoop = false;
        }
        portal.IsVisible = true;
    }

    private void OnInitiativeStateChanged()
    {
        UpdateInitiativeOnSelectedPortals();
    }

    private void UpdateInitiativeOnSelectedPortals()
    {
        var state = InitiativeTracker.SnapshotState();
        foreach (var portal in Portals)
        {
            if (!portal.IsSelectedForInitiative)
            {
                continue;
            }

            _portalHost.SetContentInitiativeOverlay(
                portal.PortalNumber,
                state,
                overlayOpacity: InitiativeTracker.OverlayOpacity,
                fontSize: InitiativeTracker.PortalFontSize);
        }
    }

    private void ApplyInitiativeToPortal(PortalRowViewModel portal)
    {
        portal.IsSelectedForCurrentMedia = false;

        _portalHost.SetContentInitiativeOverlay(
            portal.PortalNumber,
            InitiativeTracker.SnapshotState(),
            overlayOpacity: InitiativeTracker.OverlayOpacity,
            fontSize: InitiativeTracker.PortalFontSize);
        _portalHost.SetVisibility(portal.PortalNumber, true);
        portal.IsVisible = true;

        // Keep existing media assignment as the background; reflect overlay in the label.
        portal.CurrentAssignment = string.IsNullOrWhiteSpace(portal.AssignedMediaFilePath)
            ? "Initiative"
            : $"{portal.CurrentAssignment} + Initiative";
    }

    private static async Task UpdatePortalVideoSnapshotAsync(PortalRowViewModel portal, string expectedFilePath)
    {
        // Snapshot is done with a dedicated hidden LibVLC instance (no portal playback side-effects).
        var snap = await VideoSnapshotService.CaptureFirstFrameAsync(expectedFilePath, maxWidth: 512, maxHeight: 288, seekTimeMs: 1000).ConfigureAwait(false);
        if (snap is null)
        {
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!PortalSnapshotRules.ShouldApplyVideoSnapshot(portal.AssignedMediaFilePath, portal.IsVideoAssigned, expectedFilePath))
            {
                snap.Bitmap.Dispose();
                return;
            }

            portal.AssignedPreview?.Dispose();
            portal.AssignedPreview = snap.Bitmap;
        });
    }

    private void ApplyVideoOptionsToAssignedPortals()
    {
        if (Media.SelectedItem?.IsVideo != true)
        {
            return;
        }

        var selectedPath = Media.SelectedItem.FilePath;
        foreach (var portal in Portals)
        {
            if (string.Equals(portal.AssignedMediaFilePath, selectedPath, StringComparison.OrdinalIgnoreCase))
            {
                _portalHost.SetVideoLoop(portal.PortalNumber, IsVideoLoop);
            }
        }
    }

    private void RestorePreviousContent(PortalRowViewModel portal)
    {
        PortalContentRestorationPlanner.Snapshot? snapshot = null;
        if (_portalHistory.TryGetValue(portal.PortalNumber, out var stack) && stack.Count > 0)
        {
            snapshot = stack.Pop();
        }

        var plan = PortalContentRestorationPlanner.ComputeFromSnapshot(snapshot, idleTitle: "Idle");

        portal.IsVisible = plan.IsVisible;
        portal.CurrentAssignment = plan.CurrentAssignment;
        portal.AssignedMediaFilePath = plan.AssignedMediaFilePath;
        portal.IsVideoLoop = plan.IsVideoLoop;
        portal.ScaleMode = plan.ScaleMode;
        portal.Align = plan.Align;

        switch (plan.Kind)
        {
            case PortalContentRestorationPlanner.ContentKind.Video:
                portal.AssignedPreview = null;
                _portalHost.SetContentVideo(portal.PortalNumber, plan.AssignedMediaFilePath!, plan.CurrentAssignment, portal.ScaleMode, portal.Align, loop: plan.IsVideoLoop);
                var vidEffects = LookupEffectsForFile(plan.AssignedMediaFilePath!);
                _portalHost.SetOverlayEffects(portal.PortalNumber, vidEffects);
                portal.OverlayEffects = vidEffects;
                portal.IsVideoPlaying = false;
                _portalHost.SeekVideo(portal.PortalNumber, 1000);
                _ = UpdatePortalVideoSnapshotAsync(portal, plan.AssignedMediaFilePath!);
                break;
            case PortalContentRestorationPlanner.ContentKind.Image:
                portal.SetAssignedPreviewFromFile(plan.AssignedMediaFilePath!);
                _portalHost.SetContentImage(portal.PortalNumber, plan.AssignedMediaFilePath!, plan.CurrentAssignment, portal.ScaleMode, portal.Align);
                var imgEffects = LookupEffectsForFile(plan.AssignedMediaFilePath!);
                _portalHost.SetOverlayEffects(portal.PortalNumber, imgEffects);
                portal.OverlayEffects = imgEffects;
                portal.IsVideoPlaying = false;
                portal.IsVideoLoop = false;
                break;
            default:
                portal.AssignedPreview = null;
                portal.IsVideoPlaying = false;
                portal.IsVideoLoop = false;
                _portalHost.SetContentText(portal.PortalNumber, plan.CurrentAssignment);
                _portalHost.SetOverlayEffects(portal.PortalNumber, OverlayEffectsState.None);
                portal.OverlayEffects = OverlayEffectsState.None;
                break;
        }

        _portalHost.SetVisibility(portal.PortalNumber, plan.IsVisible);
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
        portal.IsSelectedForInitiative = false;
        portal.IsVideoPlaying = false;
        portal.IsVideoLoop = false;

        _portalHost.ClearContent(portal.PortalNumber);
        _portalHost.SetOverlayEffects(portal.PortalNumber, OverlayEffectsState.None);
        portal.OverlayEffects = OverlayEffectsState.None;
        UpdatePortalMediaSelectionFlags();
    }

    private OverlayEffectsState LookupEffectsForFile(string filePath)
    {
        var match = Media.Items.FirstOrDefault(i =>
            string.Equals(i.FilePath, filePath, StringComparison.OrdinalIgnoreCase));

        return match is null ? OverlayEffectsState.None : BuildEffectsState(match);
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
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnPortalClosed(portalNumber));
            return;
        }

        var row = Portals.FirstOrDefault(p => p.PortalNumber == portalNumber);
        if (row is null)
        {
            return;
        }

        _portalHistory.Remove(portalNumber);

        row.DeleteRequested -= OnDeletePortalRequested;
        try
        {
            row.Dispose();
        }
        catch
        {
            // ignore
        }
        Portals.Remove(row);

        UpdatePortalMediaSelectionFlags();
    }
}
