using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ScryScreen.App.ViewModels;
using ScryScreen.App.Views;

namespace ScryScreen.App.Services;

public sealed class PortalHostService
{
    private readonly Window _owner;
    private readonly Dictionary<int, PortalWindowController> _portals = new();

    public event Action<int>? PortalClosed;

    public PortalHostService(Window owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    public IReadOnlyList<ScreenInfoViewModel> GetScreens()
    {
        var screens = _owner.Screens;
        if (screens is null)
        {
            return Array.Empty<ScreenInfoViewModel>();
        }

        var all = screens.All
            .Select((s, index) => (Screen: s, Index: index))
            .ToList();

        var total = all.Count;
        return all
            .Select(x => new ScreenInfoViewModel(x.Index, x.Screen, total))
            .OrderByDescending(x => x.IsPrimary)
            .ThenBy(x => x.Index)
            .ToList();
    }

    public PortalWindowViewModel CreatePortal(int portalNumber, ScreenInfoViewModel? initialScreen)
    {
        if (_portals.ContainsKey(portalNumber))
        {
            throw new InvalidOperationException($"Portal {portalNumber} already exists.");
        }

        var portalVm = new PortalWindowViewModel(portalNumber);
        var portalWindow = new PortalWindow
        {
            DataContext = portalVm,
        };

        var controller = new PortalWindowController(portalNumber, portalWindow, portalVm);
        _portals.Add(portalNumber, controller);

        portalWindow.Closed += (_, _) =>
        {
            if (_portals.Remove(portalNumber))
            {
                try
                {
                    portalVm.Dispose();
                }
                catch
                {
                    // ignore cleanup failures
                }
                PortalClosed?.Invoke(portalNumber);
            }
        };

        portalWindow.Opened += (_, _) =>
        {
            if (initialScreen is not null)
            {
                AssignToScreen(portalNumber, initialScreen);
            }
            else
            {
                // Put it somewhere predictable if screens are not known yet.
                portalWindow.Position = new PixelPoint(20 + (portalNumber * 30), 20 + (portalNumber * 30));
            }
        };

        portalWindow.Show();

        // Ensure the GM console stays on top, even if a portal was created on the same monitor.
        _owner.Activate();
        return portalVm;
    }

    public void ClosePortal(int portalNumber)
    {
        if (_portals.TryGetValue(portalNumber, out var controller))
        {
            controller.Window.Close();
        }
    }

    public void CloseAll()
    {
        foreach (var portalNumber in _portals.Keys.ToArray())
        {
            ClosePortal(portalNumber);
        }
    }

    public void AssignToScreen(int portalNumber, ScreenInfoViewModel screen)
    {
        if (!_portals.TryGetValue(portalNumber, out var controller))
        {
            return;
        }

        var bounds = screen.Bounds;

        // IMPORTANT: position is in pixels; size is in DIPs.
        // For reliable multi-monitor moves on Windows, restore -> move/resize -> fullscreen.
        controller.Window.WindowState = WindowState.Normal;
        controller.Window.Position = bounds.Position;

        var scaling = screen.Scaling <= 0 ? 1.0 : screen.Scaling;
        controller.Window.Width = bounds.Width / scaling;
        controller.Window.Height = bounds.Height / scaling;

        controller.Window.WindowState = WindowState.FullScreen;

        controller.ViewModel.ScreenName = screen.DisplayName;

        // Fullscreen transitions can pull z-order forward; keep the GM console visible.
        _owner.Activate();
    }

    public void SetVisibility(int portalNumber, bool isVisible)
    {
        if (_portals.TryGetValue(portalNumber, out var controller))
        {
            controller.ViewModel.IsContentVisible = isVisible;
        }
    }

    public void SetContentText(int portalNumber, string contentTitle)
    {
        if (_portals.TryGetValue(portalNumber, out var controller))
        {
            controller.ViewModel.SetText(contentTitle);
            controller.ViewModel.IsContentVisible = true;
        }
    }

    public void ClearContent(int portalNumber, string contentTitle = "Idle")
    {
        if (_portals.TryGetValue(portalNumber, out var controller))
        {
            controller.ViewModel.ClearContent(contentTitle);
            controller.ViewModel.IsContentVisible = true;
        }
    }

    public void SetContentImage(int portalNumber, string filePath, string? contentTitle = null, MediaScaleMode scaleMode = MediaScaleMode.FillHeight, MediaAlign align = MediaAlign.Center)
    {
        if (!_portals.TryGetValue(portalNumber, out var controller))
        {
            return;
        }

        Bitmap? bitmap = null;
        try
        {
            using var stream = File.OpenRead(filePath);
            bitmap = new Bitmap(stream);
        }
        catch
        {
            // Ignore load failures; leave existing content as-is.
            return;
        }

        controller.ViewModel.ScaleMode = scaleMode;
        controller.ViewModel.Align = align;
        controller.ViewModel.SetImage(bitmap, contentTitle ?? Path.GetFileName(filePath));
        controller.ViewModel.IsContentVisible = true;
        controller.ViewModel.IsSetup = false;
    }

    public void SetContentVideo(int portalNumber, string filePath, string? contentTitle = null, MediaScaleMode scaleMode = MediaScaleMode.FillHeight, MediaAlign align = MediaAlign.Center, bool loop = false)
    {
        if (!_portals.TryGetValue(portalNumber, out var controller))
        {
            return;
        }

        controller.ViewModel.ScaleMode = scaleMode;
        controller.ViewModel.Align = align;
        controller.ViewModel.SetVideo(filePath, contentTitle ?? Path.GetFileName(filePath), loop);
        controller.ViewModel.IsContentVisible = true;
        controller.ViewModel.IsSetup = false;
    }

    public bool ToggleVideoPlayPause(int portalNumber)
    {
        if (_portals.TryGetValue(portalNumber, out var controller))
        {
            return controller.ViewModel.ToggleVideoPlayPause();
        }

        return false;
    }

    public void RestartVideo(int portalNumber)
    {
        if (_portals.TryGetValue(portalNumber, out var controller))
        {
            controller.ViewModel.RestartVideo();
        }
    }

    public void SetVideoLoop(int portalNumber, bool loop)
    {
        if (_portals.TryGetValue(portalNumber, out var controller))
        {
            controller.ViewModel.SetVideoLoop(loop);
        }
    }

    public Task<Bitmap?> CaptureVideoPreviewAsync(int portalNumber, int maxWidth = 640, int maxHeight = 360)
    {
        if (_portals.TryGetValue(portalNumber, out var controller))
        {
            return controller.ViewModel.CaptureVideoPreviewAsync(maxWidth, maxHeight);
        }

        return Task.FromResult<Bitmap?>(null);
    }

    public (long TimeMs, long LengthMs, bool IsPlaying) GetVideoState(int portalNumber)
    {
        if (_portals.TryGetValue(portalNumber, out var controller))
        {
            return controller.ViewModel.GetVideoState();
        }

        return (0, 0, false);
    }

    public void SeekVideo(int portalNumber, long timeMs)
    {
        if (_portals.TryGetValue(portalNumber, out var controller))
        {
            controller.ViewModel.SeekVideo(timeMs);
        }
    }

    public (int Width, int Height) GetVideoPixelSize(int portalNumber)
    {
        if (_portals.TryGetValue(portalNumber, out var controller))
        {
            if (controller.ViewModel.TryGetVideoPixelSize(out var w, out var h))
            {
                return (w, h);
            }
        }

        return (0, 0);
    }

    public void SetDisplayOptions(int portalNumber, MediaScaleMode scaleMode, MediaAlign align)
    {
        if (_portals.TryGetValue(portalNumber, out var controller))
        {
            controller.ViewModel.ScaleMode = scaleMode;
            controller.ViewModel.Align = align;
        }
    }

    public async Task IdentifyAllAsync(int milliseconds = 1200)
    {
        var controllers = _portals.Values.ToArray();
        foreach (var c in controllers)
        {
            c.ViewModel.ShowIdentifyOverlay();
        }

        await Task.Delay(milliseconds).ConfigureAwait(false);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            foreach (var c in controllers)
            {
                c.ViewModel.HideIdentifyOverlay();
            }
        });
    }

    private sealed record PortalWindowController(int PortalNumber, PortalWindow Window, PortalWindowViewModel ViewModel);
}
