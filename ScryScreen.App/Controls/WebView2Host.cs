using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Microsoft.Web.WebView2.Core;

namespace ScryScreen.App.Controls;

public class WebView2Host : NativeControlHost
{
    public static readonly StyledProperty<string?> HtmlProperty =
        AvaloniaProperty.Register<WebView2Host, string?>(nameof(Html));

    private IntPtr _hwnd;
    private CoreWebView2Controller? _controller;
    private CoreWebView2? _webView;
    private bool _isInitialized;
    private string? _pendingPostMessage;

    public event EventHandler<string>? WebMessageReceived;

    public void PostWebMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        if (_webView is null)
        {
            _pendingPostMessage = message;
            return;
        }

        try
        {
            _webView.PostWebMessageAsString(message);
        }
        catch
        {
            // ignore
        }
    }

    static WebView2Host()
    {
        HtmlProperty.Changed.AddClassHandler<WebView2Host>((host, _) => host.NavigateIfReady());
    }

    public string? Html
    {
        get => GetValue(HtmlProperty);
        set => SetValue(HtmlProperty, value);
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        if (!OperatingSystem.IsWindows())
        {
            return base.CreateNativeControlCore(parent);
        }

        _hwnd = Win32.CreateChildWindow(parent.Handle);
        if (_hwnd == IntPtr.Zero)
        {
            return base.CreateNativeControlCore(parent);
        }

        _ = InitializeAsync();
        return new PlatformHandle(_hwnd, "HWND");
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        try
        {
            if (_webView is not null)
            {
                _webView.WebMessageReceived -= OnWebMessageReceived;
            }

            _controller?.Close();
            _controller = null;
            _webView = null;
            _isInitialized = false;
        }
        catch
        {
            // ignore
        }

        if (OperatingSystem.IsWindows() && _hwnd != IntPtr.Zero)
        {
            Win32.DestroyWindow(_hwnd);
        }

        _hwnd = IntPtr.Zero;
        base.DestroyNativeControlCore(control);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var s = base.ArrangeOverride(finalSize);
        UpdateBounds();
        return s;
    }

    private async Task InitializeAsync()
    {
        if (!OperatingSystem.IsWindows() || _hwnd == IntPtr.Zero || _isInitialized)
        {
            return;
        }

        try
        {
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ScryScreen",
                "WebView2");

            Directory.CreateDirectory(userDataFolder);

            var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            _controller = await env.CreateCoreWebView2ControllerAsync(_hwnd);
            _webView = _controller.CoreWebView2;
            _isInitialized = true;

            _controller.IsVisible = true;
            UpdateBounds();

            try
            {
                _webView.Settings.AreDefaultContextMenusEnabled = false;
                _webView.Settings.AreDevToolsEnabled = true;
                _webView.Settings.AreBrowserAcceleratorKeysEnabled = false;
            }
            catch
            {
                // ignore
            }

            _webView.WebMessageReceived += OnWebMessageReceived;
            NavigateIfReady();

            if (!string.IsNullOrWhiteSpace(_pendingPostMessage))
            {
                var msg = _pendingPostMessage;
                _pendingPostMessage = null;
                PostWebMessage(msg);
            }
        }
        catch
        {
            // If WebView2 runtime is missing or init fails, just no-op.
        }
    }

    private void UpdateBounds()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        if (_controller is null)
        {
            return;
        }

        var scale = (VisualRoot as TopLevel)?.RenderScaling ?? 1.0;
        var w = (int)Math.Max(1, Bounds.Width * scale);
        var h = (int)Math.Max(1, Bounds.Height * scale);
        _controller.Bounds = new System.Drawing.Rectangle(0, 0, w, h);
    }

    private void NavigateIfReady()
    {
        if (_webView is null)
        {
            return;
        }

        var html = Html;
        if (string.IsNullOrWhiteSpace(html))
        {
            return;
        }

        try
        {
            _webView.NavigateToString(html);
        }
        catch
        {
            // ignore
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var message = e.TryGetWebMessageAsString();
            if (!string.IsNullOrWhiteSpace(message))
            {
                WebMessageReceived?.Invoke(this, message);
            }
        }
        catch
        {
            // ignore
        }
    }

    private static class Win32
    {
        private const int WS_CHILD = 0x40000000;
        private const int WS_VISIBLE = 0x10000000;

        public static IntPtr CreateChildWindow(IntPtr parentHwnd)
        {
            return CreateWindowEx(
                0,
                "Static",
                "",
                WS_CHILD | WS_VISIBLE,
                0,
                0,
                1,
                1,
                parentHwnd,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero);
        }

        public static void DestroyWindow(IntPtr hwnd)
        {
            _ = DestroyWindowNative(hwnd);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
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

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyWindowNative(IntPtr hWnd);
    }
}
