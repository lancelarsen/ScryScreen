using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using LibVLCSharp.Shared;

namespace ScryScreen.App.Services;

public static class VideoSnapshotService
{
    private static readonly SemaphoreSlim SnapshotGate = new(1, 1);

    public sealed record VideoSnapshot(Bitmap Bitmap, int VideoWidth, int VideoHeight);

    public static async Task<VideoSnapshot?> CaptureFirstFrameAsync(
        string filePath,
        int maxWidth = 512,
        int maxHeight = 288,
        int seekTimeMs = 1000,
        int warmupMs = 200,
        int postSeekWarmupMs = 120,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return null;
        }

        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        await SnapshotGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var tempPath = Path.Combine(Path.GetTempPath(), $"scry_video_thumb_{Guid.NewGuid():N}.png");

            IntPtr hwnd = IntPtr.Zero;
            LibVLC? libVlc = null;
            MediaPlayer? player = null;
            Media? media = null;

            try
            {
                // Prevent the file path/title overlay and keep this silent.
                libVlc = new LibVLC("--no-video-title-show", "--no-audio", "--quiet");
                player = new MediaPlayer(libVlc);

                // Hidden window for LibVLC to render into (prevents spawning its own window).
                hwnd = CreateHiddenHwnd();
                if (hwnd != IntPtr.Zero)
                {
                    player.Hwnd = hwnd;
                }

                media = new Media(libVlc, new Uri(filePath));
                player.Media = media;

                try
                {
                    player.Play();
                }
                catch
                {
                    return null;
                }

                // Give VLC a moment to decode a frame.
                try
                {
                    await Task.Delay(warmupMs, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    // cancellation
                    return null;
                }

                // Seek away from 0s (many videos have a black/blank/transition frame at t=0).
                try
                {
                    if (seekTimeMs > 0)
                    {
                        // Clamp if VLC knows the length.
                        var len = player.Length;
                        var target = (long)seekTimeMs;
                        if (len > 0 && target > len)
                        {
                            target = Math.Max(0, len - 50);
                        }

                        player.Time = target;
                        await Task.Delay(postSeekWarmupMs, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch
                {
                    // ignore
                }

                int videoW = 0;
                int videoH = 0;
                try
                {
                    uint w = 0;
                    uint h = 0;
                    if (player.Size(0, ref w, ref h) && w > 0 && h > 0)
                    {
                        videoW = (int)w;
                        videoH = (int)h;
                    }
                }
                catch
                {
                    // ignore
                }

                try
                {
                    player.TakeSnapshot(0, tempPath, (uint)maxWidth, (uint)maxHeight);
                }
                catch
                {
                    return null;
                }

                // Snapshot writing is async inside VLC; wait briefly for the file to appear.
                for (var i = 0; i < 20; i++)
                {
                    if (File.Exists(tempPath))
                    {
                        break;
                    }

                    await Task.Delay(25, cancellationToken).ConfigureAwait(false);
                }

                if (!File.Exists(tempPath))
                {
                    return null;
                }

                await using var stream = File.OpenRead(tempPath);
                var bmp = new Bitmap(stream);

                // If VLC didn't report size yet, fall back to the snapshot size.
                if (videoW <= 0 || videoH <= 0)
                {
                    videoW = bmp.PixelSize.Width;
                    videoH = bmp.PixelSize.Height;
                }

                return new VideoSnapshot(bmp, videoW, videoH);
            }
            finally
            {
                try
                {
                    if (player is not null)
                    {
                        player.Stop();
                    }
                }
                catch
                {
                    // ignore
                }

                try
                {
                    media?.Dispose();
                }
                catch
                {
                    // ignore
                }

                try
                {
                    player?.Dispose();
                }
                catch
                {
                    // ignore
                }

                try
                {
                    libVlc?.Dispose();
                }
                catch
                {
                    // ignore
                }

                if (hwnd != IntPtr.Zero)
                {
                    DestroyWindow(hwnd);
                }

                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch
                {
                    // ignore
                }
            }
        }
        finally
        {
            SnapshotGate.Release();
        }
    }

    private static IntPtr CreateHiddenHwnd()
    {
        // A tiny, hidden tool window keeps LibVLC from creating its own top-level window.
        var hwnd = CreateWindowEx(
            WS_EX_TOOLWINDOW,
            "Static",
            "",
            WS_POPUP,
            -10000,
            -10000,
            1,
            1,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);

        if (hwnd == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        ShowWindow(hwnd, SW_HIDE);
        return hwnd;
    }

    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int SW_HIDE = 0;

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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
