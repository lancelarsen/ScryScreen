using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
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
            controller.ViewModel.ContentTitle = contentTitle;
            controller.ViewModel.IsContentVisible = true;
            controller.ViewModel.IsSetup = false;
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
