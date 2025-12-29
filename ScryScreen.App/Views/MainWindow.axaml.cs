using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using System.Linq;
using System.ComponentModel;
using ScryScreen.App.ViewModels;
using Avalonia;
using System;

namespace ScryScreen.App.Views;

public partial class MainWindow : Window
{
    private bool _pinning;
    private MainWindowViewModel? _vm;

    public MainWindow()
    {
        InitializeComponent();
        Closing += OnClosing;

        Opened += (_, _) => DockToTopFullWidth();
        PositionChanged += (_, _) => DockToTopFullWidth();
        PropertyChanged += (_, _) => DockToTopFullWidth();
        DataContextChanged += (_, _) => HookViewModel();
        HookViewModel();
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
        var targetWidth = workingArea.Width / scaling;
        var targetMaxHeight = workingArea.Height / scaling;

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
                // Allow a small epsilon to avoid churn from fractional DIP conversions.
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

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            AllowMultiple = false,
            Title = "Import Media Folder",
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
}