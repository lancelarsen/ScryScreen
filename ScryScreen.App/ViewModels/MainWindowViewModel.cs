using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScryScreen.App.Models;
using ScryScreen.App.Services;
using ScryScreen.App.Utilities;
using ScryScreen.Core.Utilities;

namespace ScryScreen.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private static readonly JsonSerializerOptions EffectsJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private readonly PortalHostService _portalHost;
    private readonly EffectsAudioService _effectsAudio;
    private readonly ObservableCollection<ScreenInfoViewModel> _screens;
    private readonly ReadOnlyObservableCollection<ScreenInfoViewModel> _screensReadOnly;
    private string? _lastSelectedMediaPath;
    private string? _lastMediaFolderPath;
    private string? _lastInitiativeConfigSaveFileName;
    private string? _lastEffectsConfigSaveFileName;
    private string? _lastInitiativeConfigSavePath;
    private string? _lastEffectsConfigSavePath;

    [ObservableProperty]
    private bool autoSaveInitiativeEnabled;

    [ObservableProperty]
    private bool autoSaveEffectsEnabled;

    private int _autoSaveSuppressCount;
    private bool _initiativeAutoSaveDirty;
    private bool _effectsAutoSaveDirty;
    private string? _initiativeAutoSaveJsonSnapshot;
    private string? _effectsAutoSaveJsonSnapshot;
    private System.Threading.CancellationTokenSource? _initiativeAutoSaveCts;
    private System.Threading.CancellationTokenSource? _effectsAutoSaveCts;

    private readonly InitiativeTrackerViewModel _initiativeTracker;

    public EffectsSettingsViewModel Effects { get; }

    private readonly System.Collections.Generic.Dictionary<int, System.Collections.Generic.Stack<PortalContentRestorationPlanner.Snapshot>> _portalHistory = new();

    public MainWindowViewModel(PortalHostService portalHost)
    {
        _portalHost = portalHost ?? throw new ArgumentNullException(nameof(portalHost));
        _effectsAudio = new EffectsAudioService();
        _screens = new ObservableCollection<ScreenInfoViewModel>(_portalHost.GetScreens().ToList());
        _screensReadOnly = new ReadOnlyObservableCollection<ScreenInfoViewModel>(_screens);
        Portals = new ObservableCollection<PortalRowViewModel>();
        Media = new MediaLibraryViewModel();

        Effects = new EffectsSettingsViewModel();
        Effects.PropertyChanged += OnGlobalEffectsChanged;

        Portals.CollectionChanged += (_, _) => UpdateEffectsAudioMix();

        _initiativeTracker = new InitiativeTrackerViewModel();
        _initiativeTracker.StateChanged += OnInitiativeStateChanged;

        SelectedScaleMode = MediaScaleMode.FillHeight;
        SelectedAlign = MediaAlign.Center;

        Media.PropertyChanged += OnMediaPropertyChanged;

        _portalHost.PortalClosed += OnPortalClosed;

        // Start with one portal for convenience.
        AddPortal();

        // Ensure portals start with a consistent effects state.
        ApplyEffectsToAllPortals();
    }

    private void OnGlobalEffectsChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Any effects setting change updates portals + auto-save.
        ApplyEffectsToAllPortals();

        // Enabled/sound toggles are intentionally NOT persisted.
        if (IsPersistableEffectsProperty(e.PropertyName))
        {
            NotifyEffectsChangedForAutoSave();
        }
    }

    private static bool IsPersistableEffectsProperty(string? propertyName)
    {
        // Persist numeric/range changes; do NOT persist enabled toggles or sound toggles.
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return true;
        }

        if (propertyName == nameof(EffectsSettingsViewModel.EffectsVolume))
        {
            return true;
        }

        if (propertyName.EndsWith("Enabled", StringComparison.Ordinal) ||
            propertyName.EndsWith("SoundEnabled", StringComparison.Ordinal))
        {
            return false;
        }

        return propertyName.EndsWith("Min", StringComparison.Ordinal) ||
               propertyName.EndsWith("Max", StringComparison.Ordinal) ||
               propertyName.EndsWith("Intensity", StringComparison.Ordinal);
    }

    public ReadOnlyObservableCollection<ScreenInfoViewModel> Screens => _screensReadOnly;

    public string? LastInitiativeConfigSaveFileName
    {
        get => _lastInitiativeConfigSaveFileName;
        internal set => SetProperty(ref _lastInitiativeConfigSaveFileName, value);
    }

    public string? LastEffectsConfigSaveFileName
    {
        get => _lastEffectsConfigSaveFileName;
        internal set => SetProperty(ref _lastEffectsConfigSaveFileName, value);
    }

    public string? LastInitiativeConfigSavePath
    {
        get => _lastInitiativeConfigSavePath;
        internal set => SetProperty(ref _lastInitiativeConfigSavePath, value);
    }

    public string? LastEffectsConfigSavePath
    {
        get => _lastEffectsConfigSavePath;
        internal set => SetProperty(ref _lastEffectsConfigSavePath, value);
    }

    internal IDisposable SuppressAutoSave()
    {
        _autoSaveSuppressCount++;
        return new AutoSaveSuppressionToken(this);
    }

    private bool IsAutoSaveSuppressed => _autoSaveSuppressCount > 0;

    private sealed class AutoSaveSuppressionToken : IDisposable
    {
        private MainWindowViewModel? _vm;

        public AutoSaveSuppressionToken(MainWindowViewModel vm)
        {
            _vm = vm;
        }

        public void Dispose()
        {
            var vm = _vm;
            _vm = null;
            if (vm is null)
            {
                return;
            }

            if (vm._autoSaveSuppressCount > 0)
            {
                vm._autoSaveSuppressCount--;
            }
        }
    }

    public void RefreshScreens()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(RefreshScreens);
            return;
        }

        // IMPORTANT: Rebuilding the ItemsSource can cause the ComboBox to temporarily clear
        // SelectedItem (because the old item is no longer present), and with TwoWay binding
        // that null can flow back into the VM. Snapshot the previous selection per portal so
        // we can restore it after the list is repopulated.
        var previousSelections = Portals.ToDictionary(p => p.PortalNumber, p => p.SelectedScreen);

        var refreshed = _portalHost.GetScreens().ToList();

        _screens.Clear();
        foreach (var screen in refreshed)
        {
            _screens.Add(screen);
        }

        if (_screens.Count == 0)
        {
            // Nothing to select; keep portals as-is.
            return;
        }

        var primary = _screens.FirstOrDefault(s => s.IsPrimary) ?? _screens[0];

        foreach (var portal in Portals)
        {
            previousSelections.TryGetValue(portal.PortalNumber, out var previous);
            if (previous is null)
            {
                // Preserve intentionally-unassigned portals.
                continue;
            }

            var match = FindBestScreenMatch(previous, _screens);
            portal.SelectedScreen = match ?? primary;
        }
    }

    private static ScreenInfoViewModel? FindBestScreenMatch(ScreenInfoViewModel previous, System.Collections.Generic.IReadOnlyList<ScreenInfoViewModel> candidates)
    {
        var previousCandidate = new ScreenMatchCandidate(
            PlatformDisplayName: previous.PlatformDisplayName,
            Bounds: previous.Bounds,
            Scaling: previous.Scaling,
            IsPrimary: previous.IsPrimary);

        var candidateList = candidates
            .Select(s => (
                Candidate: new ScreenMatchCandidate(
                    PlatformDisplayName: s.PlatformDisplayName,
                    Bounds: s.Bounds,
                    Scaling: s.Scaling,
                    IsPrimary: s.IsPrimary),
                Value: s))
            .ToList();

        return ScreenMatching.FindBestMatch(previousCandidate, candidateList);
    }

    public ObservableCollection<PortalRowViewModel> Portals { get; }

    public MediaLibraryViewModel Media { get; }

    public string? LastMediaFolderPath => _lastMediaFolderPath;

    public string? LastSelectedMediaPath => _lastSelectedMediaPath;

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

            var selectedPath = Media.SelectedItem?.FilePath;
            if (_lastSelectedMediaPath is null && selectedPath is not null && !IsControlsSectionVisible)
            {
                IsControlsSectionVisible = true;
            }

            _lastSelectedMediaPath = selectedPath;
        }
    }

    private void NotifyEffectsChangedForAutoSave()
    {
        _effectsAutoSaveDirty = true;

        // Cache a snapshot for shutdown timing.
        var snapshot = ExportSelectedEffectsConfigJson(indented: false);
        if (!string.IsNullOrWhiteSpace(snapshot))
        {
            _effectsAutoSaveJsonSnapshot = snapshot;
        }

        if (IsAutoSaveSuppressed || !AutoSaveEffectsEnabled)
        {
            return;
        }

        var savePath = LastEffectsConfigSavePath;
        if (string.IsNullOrWhiteSpace(savePath))
        {
            return;
        }

        ScheduleDebouncedEffectsAutoSave(savePath);
    }

    private void ScheduleDebouncedEffectsAutoSave(string savePath)
    {
        _effectsAutoSaveCts?.Cancel();
        _effectsAutoSaveCts = new System.Threading.CancellationTokenSource();
        var token = _effectsAutoSaveCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(750, token).ConfigureAwait(false);
                if (token.IsCancellationRequested)
                {
                    return;
                }

                await AutoSaveEffectsToPathAsync(savePath, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // ignore
            }
            catch (Exception ex)
            {
                ErrorReporter.Report(ex, "Auto-save Effects");
            }
        }, token);
    }

    internal void AutoSaveEffectsImmediatelyIfEnabled(bool ignoreSuppression = false)
    {
        if (!AutoSaveEffectsEnabled)
        {
            return;
        }

        if (!ignoreSuppression && IsAutoSaveSuppressed)
        {
            return;
        }

        var savePath = LastEffectsConfigSavePath;
        if (string.IsNullOrWhiteSpace(savePath))
        {
            return;
        }

        try
        {
            var json = ExportSelectedEffectsConfigJson(indented: false);
            if (string.IsNullOrWhiteSpace(json))
            {
                json = _effectsAutoSaveJsonSnapshot;
                if (string.IsNullOrWhiteSpace(json))
                {
                    return;
                }
            }

            var dir = Path.GetDirectoryName(savePath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(savePath, json);
            _effectsAutoSaveDirty = false;
        }
        catch (Exception ex)
        {
            ErrorReporter.Report(ex, "Auto-save Effects");
        }
    }

    private async Task AutoSaveEffectsToPathAsync(string savePath, System.Threading.CancellationToken token)
    {
        if (!AutoSaveEffectsEnabled || !_effectsAutoSaveDirty)
        {
            return;
        }

        if (IsAutoSaveSuppressed)
        {
            return;
        }

        var json = await Dispatcher.UIThread.InvokeAsync(() => ExportSelectedEffectsConfigJson(indented: false));
        if (string.IsNullOrWhiteSpace(json))
        {
            json = _effectsAutoSaveJsonSnapshot;
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }
        }

        var dir = Path.GetDirectoryName(savePath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        await File.WriteAllTextAsync(savePath, json, token).ConfigureAwait(false);
        _effectsAutoSaveDirty = false;
    }

    private long _lightningTriggerNonce;
    private long _quakeTriggerNonce;

    private OverlayEffectsState BuildEffectsStateFromGlobal()
    {
        static double ClampMin0(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v))
            {
                return 0;
            }

            return v < 0 ? 0 : v;
        }

        var fx = Effects;
        return new OverlayEffectsState(
            RainEnabled: fx.RainEnabled,
            RainIntensity: ClampMin0(fx.RainIntensity),
            RainSoundEnabled: fx.RainSoundEnabled,
            SnowEnabled: fx.SnowEnabled,
            SnowIntensity: ClampMin0(fx.SnowIntensity),
            SnowSoundEnabled: fx.SnowSoundEnabled,
            AshEnabled: fx.AshEnabled,
            AshIntensity: ClampMin0(fx.AshIntensity),
            AshSoundEnabled: fx.AshSoundEnabled,
            FireEnabled: fx.FireEnabled,
            FireIntensity: ClampMin0(fx.FireIntensity),
            FireSoundEnabled: fx.FireSoundEnabled,
            SandEnabled: fx.SandEnabled,
            SandIntensity: ClampMin0(fx.SandIntensity),
            SandSoundEnabled: fx.SandSoundEnabled,
            FogEnabled: fx.FogEnabled,
            FogIntensity: ClampMin0(fx.FogIntensity),
            FogSoundEnabled: fx.FogSoundEnabled,
            SmokeEnabled: fx.SmokeEnabled,
            SmokeIntensity: ClampMin0(fx.SmokeIntensity),
            SmokeSoundEnabled: fx.SmokeSoundEnabled,
            LightningEnabled: fx.LightningEnabled,
            LightningIntensity: ClampMin0(fx.LightningIntensity),
            LightningSoundEnabled: fx.LightningSoundEnabled,
            QuakeEnabled: fx.QuakeEnabled,
            QuakeIntensity: ClampMin0(fx.QuakeIntensity),
            QuakeSoundEnabled: fx.QuakeSoundEnabled,
            EffectsVolume: ClampMin0(fx.EffectsVolume),
            LightningTrigger: _lightningTriggerNonce,
            QuakeTrigger: _quakeTriggerNonce);
    }

    public string ExportSelectedEffectsConfigJson(bool indented)
    {
        var config = Effects.ExportEffectsConfigForPersistence();
        return JsonSerializer.Serialize(config, new JsonSerializerOptions(EffectsJsonOptions)
        {
            WriteIndented = indented,
        });
    }

    internal string ExportBestEffectsConfigJson(bool indented)
    {
        var json = ExportSelectedEffectsConfigJson(indented);
        if (!string.IsNullOrWhiteSpace(json))
        {
            return json;
        }

        // Fallback for shutdown timing: SelectedItem can be cleared before persistence runs.
        // Snapshot is stored with WriteIndented=false.
        if (string.IsNullOrWhiteSpace(_effectsAutoSaveJsonSnapshot))
        {
            return string.Empty;
        }

        if (!indented)
        {
            return _effectsAutoSaveJsonSnapshot;
        }

        try
        {
            var config = JsonSerializer.Deserialize<EffectsConfig>(_effectsAutoSaveJsonSnapshot);
            return config is null
                ? _effectsAutoSaveJsonSnapshot
                : JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return _effectsAutoSaveJsonSnapshot;
        }
    }

    internal string ExportBestInitiativeConfigJson(bool indented)
    {
        var json = InitiativeTracker.ExportConfigJson(indented);
        if (!string.IsNullOrWhiteSpace(json))
        {
            return json;
        }

        if (string.IsNullOrWhiteSpace(_initiativeAutoSaveJsonSnapshot))
        {
            return string.Empty;
        }

        if (!indented)
        {
            return _initiativeAutoSaveJsonSnapshot;
        }

        try
        {
            // InitiativeTracker's JSON schema can evolve; best effort.
            using var doc = JsonDocument.Parse(_initiativeAutoSaveJsonSnapshot);
            return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return _initiativeAutoSaveJsonSnapshot;
        }
    }

    public void ImportSelectedEffectsConfigJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        var config = JsonSerializer.Deserialize<EffectsConfig>(json, EffectsJsonOptions);
        if (config is null)
        {
            return;
        }

        Effects.ImportEffectsConfigForPersistence(config);
        ApplyEffectsToAllPortals();
    }

    private void ApplyEffectsToAllPortals()
    {
        // Build state once; apply to all portals.
        var effects = BuildEffectsStateFromGlobal();

        foreach (var portal in Portals)
        {
            _portalHost.SetOverlayEffects(portal.PortalNumber, effects);
            portal.OverlayEffects = effects;
        }

        UpdateEffectsAudioMix();
    }

    private void UpdateEffectsAudioMix()
    {
        _effectsAudio.UpdateFromPortalEffects(
            Portals.Select(p => new EffectsAudioService.PortalEffectsSnapshot(
                PortalNumber: p.PortalNumber,
                IsVisible: p.IsVisible,
                Effects: p.OverlayEffects)));
    }

    [RelayCommand]
    private void TriggerLightning()
    {
        if (!Effects.LightningEnabled)
        {
            Effects.LightningEnabled = true;
        }

        if (Effects.LightningIntensity < Effects.LightningMin)
        {
            Effects.LightningIntensity = Effects.LightningMin;
        }

        _lightningTriggerNonce++;
        ApplyEffectsToAllPortals();

        // If there is no active visible portal for this media, still play a one-shot so
        // the user gets feedback while testing. Otherwise thunder will come from portal flash events.
        if (Effects.LightningSoundEnabled && Portals.Any(p => p.IsVisible))
        {
            _effectsAudio.PlayLightningThunder(Effects.LightningIntensity);
        }
    }

    [RelayCommand]
    private void TriggerQuake()
    {
        if (!Effects.QuakeEnabled)
        {
            Effects.QuakeEnabled = true;
        }

        if (Effects.QuakeIntensity < Effects.QuakeMin)
        {
            Effects.QuakeIntensity = Effects.QuakeMin;
        }

        _quakeTriggerNonce++;
        ApplyEffectsToAllPortals();

        if (Effects.QuakeSoundEnabled)
        {
            _effectsAudio.PlayQuakeHit(Effects.QuakeIntensity);
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
        portalRow.PropertyChanged += OnPortalRowPropertyChanged;

        Portals.Add(portalRow);

        _portalHost.SetDisplayOptions(portalNumber, portalRow.ScaleMode, portalRow.Align);

        UpdatePortalMediaSelectionFlags();

        UpdateEffectsAudioMix();
    }

    private void OnPortalRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PortalRowViewModel.IsVisible) or nameof(PortalRowViewModel.OverlayEffects))
        {
            UpdateEffectsAudioMix();
        }
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

        if (!string.IsNullOrWhiteSpace(folderPath) && System.IO.Directory.Exists(folderPath))
        {
            _lastMediaFolderPath = folderPath;
        }
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
        var effects = BuildEffectsStateFromGlobal();
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

        UpdateEffectsAudioMix();
    }

    private void OnInitiativeStateChanged()
    {
        UpdateInitiativeOnSelectedPortals();
        NotifyInitiativeChangedForAutoSave();
    }

    private void NotifyInitiativeChangedForAutoSave()
    {
        _initiativeAutoSaveDirty = true;

        // Cache snapshot to survive shutdown ordering.
        var snapshot = InitiativeTracker.ExportConfigJson(indented: false);
        if (!string.IsNullOrWhiteSpace(snapshot))
        {
            _initiativeAutoSaveJsonSnapshot = snapshot;
        }

        if (IsAutoSaveSuppressed || !AutoSaveInitiativeEnabled)
        {
            return;
        }

        var savePath = LastInitiativeConfigSavePath;
        if (string.IsNullOrWhiteSpace(savePath))
        {
            return;
        }

        ScheduleDebouncedInitiativeAutoSave(savePath);
    }

    private void ScheduleDebouncedInitiativeAutoSave(string savePath)
    {
        _initiativeAutoSaveCts?.Cancel();
        _initiativeAutoSaveCts = new System.Threading.CancellationTokenSource();
        var token = _initiativeAutoSaveCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(750, token).ConfigureAwait(false);
                if (token.IsCancellationRequested)
                {
                    return;
                }

                await AutoSaveInitiativeToPathAsync(savePath, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // ignore
            }
            catch (Exception ex)
            {
                ErrorReporter.Report(ex, "Auto-save Initiative");
            }
        }, token);
    }

    internal void AutoSaveInitiativeImmediatelyIfEnabled(bool ignoreSuppression = false)
    {
        if (!AutoSaveInitiativeEnabled)
        {
            return;
        }

        if (!ignoreSuppression && IsAutoSaveSuppressed)
        {
            return;
        }

        var savePath = LastInitiativeConfigSavePath;
        if (string.IsNullOrWhiteSpace(savePath))
        {
            return;
        }

        try
        {
            var json = InitiativeTracker.ExportConfigJson(indented: false);
            if (string.IsNullOrWhiteSpace(json))
            {
                json = _initiativeAutoSaveJsonSnapshot;
                if (string.IsNullOrWhiteSpace(json))
                {
                    return;
                }
            }
            var dir = Path.GetDirectoryName(savePath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(savePath, json);
            _initiativeAutoSaveDirty = false;
        }
        catch (Exception ex)
        {
            ErrorReporter.Report(ex, "Auto-save Initiative");
        }
    }

    private async Task AutoSaveInitiativeToPathAsync(string savePath, System.Threading.CancellationToken token)
    {
        if (!AutoSaveInitiativeEnabled || !_initiativeAutoSaveDirty)
        {
            return;
        }

        if (IsAutoSaveSuppressed)
        {
            return;
        }

        var json = await Dispatcher.UIThread.InvokeAsync(() => InitiativeTracker.ExportConfigJson(indented: false));
        if (string.IsNullOrWhiteSpace(json))
        {
            json = _initiativeAutoSaveJsonSnapshot;
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }
        }
        var dir = Path.GetDirectoryName(savePath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        await File.WriteAllTextAsync(savePath, json, token).ConfigureAwait(false);
        _initiativeAutoSaveDirty = false;
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

        UpdateEffectsAudioMix();
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

        UpdateEffectsAudioMix();
    }

    private OverlayEffectsState LookupEffectsForFile(string filePath)
    {
        _ = filePath;
        return BuildEffectsStateFromGlobal();
    }

    public void Shutdown()
    {
        AutoSaveInitiativeImmediatelyIfEnabled(ignoreSuppression: true);
        AutoSaveEffectsImmediatelyIfEnabled(ignoreSuppression: true);
        LastSessionPersistence.Save(this);
        _effectsAudio.Dispose();
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
        row.PropertyChanged -= OnPortalRowPropertyChanged;
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

        UpdateEffectsAudioMix();
    }
}
