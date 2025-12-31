using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using LibVLCSharp.Shared;

namespace ScryScreen.App.Controls;

public sealed class VlcVideoHost : NativeControlHost
{
    public static readonly StyledProperty<MediaPlayer?> MediaPlayerProperty =
        AvaloniaProperty.Register<VlcVideoHost, MediaPlayer?>(nameof(MediaPlayer));

    private IntPtr _hwnd;

    public MediaPlayer? MediaPlayer
    {
        get => GetValue(MediaPlayerProperty);
        set => SetValue(MediaPlayerProperty, value);
    }

    static VlcVideoHost()
    {
        MediaPlayerProperty.Changed.AddClassHandler<VlcVideoHost>((host, _) => host.AttachPlayerIfPossible());
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        if (!OperatingSystem.IsWindows())
        {
            return base.CreateNativeControlCore(parent);
        }

        // Create a simple child HWND for LibVLC to render into.
        _hwnd = CreateWindowEx(
            0,
            "Static",
            "",
            WS_CHILD | WS_VISIBLE,
            0,
            0,
            1,
            1,
            parent.Handle,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
        {
            return base.CreateNativeControlCore(parent);
        }

        AttachPlayerIfPossible();
        return new PlatformHandle(_hwnd, "HWND");
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        DetachPlayerIfNeeded();

        if (OperatingSystem.IsWindows() && _hwnd != IntPtr.Zero)
        {
            DestroyWindow(_hwnd);
        }

        _hwnd = IntPtr.Zero;
        base.DestroyNativeControlCore(control);
    }

    private void AttachPlayerIfPossible()
    {
        if (!OperatingSystem.IsWindows() || _hwnd == IntPtr.Zero)
        {
            return;
        }

        try
        {
            var player = MediaPlayer;
            if (player is null)
            {
                return;
            }

            // Direct LibVLC to render into our hosted child window.
            player.Hwnd = _hwnd;
        }
        catch
        {
            // ignore
        }
    }

    private void DetachPlayerIfNeeded()
    {
        if (!OperatingSystem.IsWindows() || _hwnd == IntPtr.Zero)
        {
            return;
        }

        try
        {
            var player = MediaPlayer;
            if (player is null)
            {
                return;
            }

            if (player.Hwnd == _hwnd)
            {
                player.Hwnd = IntPtr.Zero;
            }
        }
        catch
        {
            // ignore
        }
    }

    private const int WS_CHILD = 0x40000000;
    private const int WS_VISIBLE = 0x10000000;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        int dwExStyle,
        string lpClassName,
        string lpWindowName,
        int dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);
}
