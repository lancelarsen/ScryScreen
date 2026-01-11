using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using System.Linq;
using System.ComponentModel;
using ScryScreen.App.ViewModels;
using Avalonia;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Text;
using ScryScreen.App.Services;
using Avalonia.Threading;

namespace ScryScreen.App.Views;

public partial class MainWindow : Window
{
    private bool _pinning;
    private MainWindowViewModel? _vm;
    private bool _screensChangedHooked;

    private static Uri ToFileUri(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.EndsWith(Path.DirectorySeparatorChar))
        {
            fullPath += Path.DirectorySeparatorChar;
        }

        return new UriBuilder
        {
            Scheme = Uri.UriSchemeFile,
            Host = string.Empty,
            Path = fullPath,
        }.Uri;
    }

    private async Task<IStorageFolder?> TryGetFolderAsync(string? directoryPath)
    {
        if (StorageProvider is null)
        {
            return null;
        }

        try
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                return null;
            }

            if (!Directory.Exists(directoryPath))
            {
                return null;
            }

            return await StorageProvider.TryGetFolderFromPathAsync(ToFileUri(directoryPath));
        }
        catch
        {
            return null;
        }
    }

    private Task<IStorageFolder?> TryGetSavesFolderAsync() =>
        TryGetFolderAsync(LastSessionPersistence.GetSavesDirectory());

    public MainWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
        Closed += OnClosed;

        TryHookScreensChanged();

        Opened += (_, _) => DockToTopFullWidth();
        DataContextChanged += (_, _) => HookViewModel();
        HookViewModel();
    }

    private void TryHookScreensChanged()
    {
        if (_screensChangedHooked)
        {
            return;
        }

        var screens = Screens;
        if (screens is null)
        {
            return;
        }

        screens.Changed += OnScreensChanged;
        _screensChangedHooked = true;
    }

    private void OnScreensChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnScreensChanged(sender, e));
            return;
        }

        _vm?.RefreshScreens();
        DockToTopFullWidth();
    }

    private void HookViewModel()
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _vm = DataContext as MainWindowViewModel;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // When sections are shown/hidden, the window height changes (SizeToContent=Height).
        // Keep the window pinned to the top of the active screen.
        if (e.PropertyName is nameof(MainWindowViewModel.IsPortalsSectionVisible)
            or nameof(MainWindowViewModel.IsLibrarySectionVisible)
            or nameof(MainWindowViewModel.IsControlsSectionVisible))
        {
            DockToTopFullWidth();
        }
    }

    private void DockToTopFullWidth()
    {
        if (_pinning)
        {
            return;
        }

        var screens = Screens;
        if (screens is null)
        {
            return;
        }

        // Always dock to the primary screen.
        // Using the window's current Position during startup can pick the wrong monitor
        // (e.g., when virtual screen coordinates don't start at 0,0).
        var currentScreen = screens.Primary;
        if (currentScreen is null)
        {
            return;
        }

        var workingArea = currentScreen.WorkingArea;

        var scaling = currentScreen.Scaling <= 0 ? 1.0 : currentScreen.Scaling;
        // Round to whole DIP units to avoid tiny DPI conversion oscillations.
        var targetWidth = Math.Round(workingArea.Width / scaling);
        var targetMaxHeight = Math.Round(workingArea.Height / scaling);

        _pinning = true;
        try
        {
            var targetX = workingArea.X;
            var targetY = workingArea.Y;
            if (Position.X != targetX || Position.Y != targetY)
            {
                Position = new PixelPoint(targetX, targetY);
            }

            if (!double.IsNaN(targetWidth) && targetWidth > 0)
            {
                if (double.IsNaN(Width) || Math.Abs(Width - targetWidth) > 0.5)
                {
                    Width = targetWidth;
                }
            }

            if (!double.IsNaN(targetMaxHeight) && targetMaxHeight > 0)
            {
                // Prevent SizeToContent from growing past the visible working area.
                if (double.IsNaN(MaxHeight) || Math.Abs(MaxHeight - targetMaxHeight) > 0.5)
                {
                    MaxHeight = targetMaxHeight;
                }
            }
        }
        finally
        {
            _pinning = false;
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.Shutdown();
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (_screensChangedHooked && Screens is not null)
        {
            Screens.Changed -= OnScreensChanged;
            _screensChangedHooked = false;
        }
    }

    private void OnCloseApp(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void OnImportMediaFolder(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        if (StorageProvider is null)
        {
            return;
        }

        var startFolder = await TryGetFolderAsync(vm.LastMediaFolderPath);

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            AllowMultiple = false,
            Title = "Import Media Folder",
            SuggestedStartLocation = startFolder,
        });

        var folder = folders.FirstOrDefault();
        if (folder is null)
        {
            return;
        }

        var path = folder.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        vm.ImportMediaFolder(path);
    }

    private async void OnSaveInitiativeConfig(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is not MainWindowViewModel vm)
            {
                return;
            }

            if (StorageProvider is null)
            {
                return;
            }

            var savesFolder = await TryGetSavesFolderAsync();

            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save Initiative Config",
                SuggestedFileName = vm.LastInitiativeConfigSaveFileName ?? "initiative.json",
                DefaultExtension = "json",
                SuggestedStartLocation = savesFolder,
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("JSON")
                    {
                        Patterns = new[] { "*.json" },
                    },
                },
            });

            if (file is null)
            {
                return;
            }

            var path = file.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            var json = vm.InitiativeTracker.ExportConfigJson(indented: true);
            await File.WriteAllTextAsync(path, json);
            vm.LastInitiativeConfigSaveFileName = Path.GetFileName(path);
        }
        catch (Exception ex)
        {
            ErrorReporter.Report(ex, "Save Initiative Config");
        }
    }

    private async void OnLoadInitiativeConfig(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is not MainWindowViewModel vm)
            {
                return;
            }

            if (StorageProvider is null)
            {
                return;
            }

            var savesFolder = await TryGetSavesFolderAsync();

            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                AllowMultiple = false,
                Title = "Load Initiative Config",
                SuggestedStartLocation = savesFolder,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("JSON")
                    {
                        Patterns = new[] { "*.json" },
                    },
                },
            });

            var file = files.FirstOrDefault();
            if (file is null)
            {
                return;
            }

            var path = file.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return;
            }

            var json = await File.ReadAllTextAsync(path);
            vm.InitiativeTracker.ImportConfigJson(json);
        }
        catch (Exception ex)
        {
            ErrorReporter.Report(ex, "Load Initiative Config");
        }
    }

    private async void OnSaveEffectsConfig(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is not MainWindowViewModel vm)
            {
                return;
            }

            if (StorageProvider is null)
            {
                return;
            }

            var savesFolder = await TryGetSavesFolderAsync();

            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save Effects Config",
                SuggestedFileName = vm.LastEffectsConfigSaveFileName ?? "effects.json",
                DefaultExtension = "json",
                SuggestedStartLocation = savesFolder,
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("JSON")
                    {
                        Patterns = new[] { "*.json" },
                    },
                },
            });

            if (file is null)
            {
                return;
            }

            var path = file.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            var json = vm.ExportSelectedEffectsConfigJson(indented: true);
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            await File.WriteAllTextAsync(path, json, Encoding.UTF8);
            vm.LastEffectsConfigSaveFileName = Path.GetFileName(path);
        }
        catch (Exception ex)
        {
            ErrorReporter.Report(ex, "Save Effects Config");
        }
    }

    private async void OnLoadEffectsConfig(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is not MainWindowViewModel vm)
            {
                return;
            }

            if (StorageProvider is null)
            {
                return;
            }

            var savesFolder = await TryGetSavesFolderAsync();

            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                AllowMultiple = false,
                Title = "Load Effects Config",
                SuggestedStartLocation = savesFolder,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("JSON")
                    {
                        Patterns = new[] { "*.json" },
                    },
                },
            });

            var file = files.FirstOrDefault();
            if (file is null)
            {
                return;
            }

            var path = file.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return;
            }

            var json = await File.ReadAllTextAsync(path, Encoding.UTF8);
            vm.ImportSelectedEffectsConfigJson(json);
        }
        catch (Exception ex)
        {
            ErrorReporter.Report(ex, "Load Effects Config");
        }
    }
}