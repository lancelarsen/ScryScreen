using Avalonia;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.ExceptionServices;
using System.Reflection;
using LibVLCSharp.Shared;
using Projektanker.Icons.Avalonia;
using Projektanker.Icons.Avalonia.MaterialDesign;

namespace ScryScreen.App;

sealed class Program
{
    private const uint MbOk = 0x00000000;
    private const uint MbIconError = 0x00000010;

    private const int S_OK = 0;
    private const int S_FALSE = 1;

    private const uint TDN_BUTTON_CLICKED = 2;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int TaskDialogCallbackProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, IntPtr lpRefData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconExW(string lpszFile, int nIconIndex, IntPtr[]? phiconLarge, IntPtr[]? phiconSmall, uint nIcons);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("comctl32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int TaskDialogIndirect(ref TASKDIALOGCONFIG pTaskConfig, out int pnButton, out int pnRadioButton, out bool pfVerificationFlagChecked);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    private const uint CfUnicodeText = 13;
    private const uint GmemMoveable = 0x0002;

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        var logPath = GetLogPath();

        var firstChanceCount = 0;
        Exception? lastFirstChance = null;

        try
        {
            SafeAppend(logPath, "[Startup] Enter Main\n");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            SafeAppend(logPath, $"\n===== ScryScreen startup {DateTime.Now:yyyy-MM-dd HH:mm:ss} =====\n");
            SafeAppend(logPath, "[Startup] Log initialized\n");

            // Use a shareable file stream so we can append from other paths (and allow readers) even during startup.
            var traceStream = new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            var traceWriter = new StreamWriter(traceStream) { AutoFlush = true };
            Trace.Listeners.Add(new TextWriterTraceListener(traceWriter));
            Trace.AutoFlush = true;

            SafeAppend(logPath, "[Startup] Trace configured\n");

            if (string.Equals(Environment.GetEnvironmentVariable("SCRYSCREEN_TEST_STARTUP_CRASH"), "1", StringComparison.OrdinalIgnoreCase))
            {
                SafeAppend(logPath, "[Startup] SCRYSCREEN_TEST_STARTUP_CRASH=1; throwing test exception\n");
                throw new InvalidOperationException("Test startup crash (SCRYSCREEN_TEST_STARTUP_CRASH=1).\nThis is intentional for testing the startup error dialog.");
            }

            // Capture exceptions that may be handled internally by frameworks but still cause a silent startup abort.
            // Keep it bounded so we don't spam the log.
            AppDomain.CurrentDomain.FirstChanceException += (_, e) =>
            {
                try
                {
                    lastFirstChance = e.Exception;

                    var n = System.Threading.Interlocked.Increment(ref firstChanceCount);
                    if (n <= 25)
                    {
                        SafeAppend(logPath, $"[FirstChance #{n}] {e.Exception}\n");
                    }
                }
                catch
                {
                    // ignore
                }
            };

            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                Trace.WriteLine($"[UnhandledException] {e.ExceptionObject}");
                SafeAppend(logPath, $"[UnhandledException] {e.ExceptionObject}\n");
                ShowStartupErrorDialog(
                    title: "ScryScreen - Fatal Error",
                    intro: "There was an error launching ScryScreen.\nSee the error information below.",
                    topLevelError: e.ExceptionObject?.ToString() ?? "Unknown fatal error",
                    details: e.ExceptionObject?.ToString() ?? string.Empty,
                    logPath: logPath);
                Environment.ExitCode = 1;
            };

            // LibVLCSharp: loads native VLC runtime (Windows MVP)
            SafeAppend(logPath, "[Startup] Initializing LibVLC\n");
            LibVLCSharp.Shared.Core.Initialize();

            SafeAppend(logPath, "[Startup] Starting Avalonia\n");

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

            SafeAppend(logPath, "[Startup] Avalonia returned\n");

            // Some startup failures can be swallowed by the framework and only reflected via ExitCode.
            if (Environment.ExitCode != 0)
            {
                SafeAppend(logPath, $"[ExitCode] {Environment.ExitCode}\n");

                var top = lastFirstChance is null
                    ? "ScryScreen exited during startup."
                    : $"{lastFirstChance.GetType().Name}: {lastFirstChance.Message}";

                ShowStartupErrorDialog(
                    title: "ScryScreen - Startup Error",
                    intro: "There was an error launching ScryScreen.\nSee the error information below.",
                    topLevelError: top,
                    details: lastFirstChance?.ToString() ?? string.Empty,
                    logPath: logPath);
            }
        }
        catch (Exception ex)
        {
            try
            {
                Trace.WriteLine($"[Fatal] {ex}");
                Trace.Flush();
                SafeAppend(logPath, $"[Fatal] {ex}\n");
            }
            catch
            {
                // ignored
            }

            ShowStartupErrorDialog(
                title: "ScryScreen - Startup Error",
                intro: "There was an error launching ScryScreen.\nSee the error information below.",
                topLevelError: $"{ex.GetType().Name}: {ex.Message}",
                details: ex.ToString(),
                logPath: logPath);

            Environment.ExitCode = 1;
            return;
        }
    }

    private static string GetLogPath()
        => Path.Combine(GetLogFolderPath(), "ScryScreen.startup.log");

    private static string GetLogFolderPath()
    {
        try
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localAppData))
            {
                return Path.Combine(localAppData, "ScryScreen", "logs");
            }
        }
        catch
        {
            // ignore
        }

        // Fallback (e.g., if LocalAppData isn't available).
        return AppContext.BaseDirectory;
    }

    private static void TryShowFatalMessageBox(string title, string message)
    {
        try
        {
            // This works even if Avalonia fails before any window exists.
            MessageBoxW(IntPtr.Zero, message, title, MbOk | MbIconError);
        }
        catch
        {
            // ignore
        }
    }

    private static void ShowStartupErrorDialog(string title, string intro, string topLevelError, string details, string logPath)
    {
        // Prefer TaskDialog on Windows (better UX: expandable details + command links).
        if (!OperatingSystem.IsWindows())
        {
            // Keep this concise: full details are written to the startup log.
            TryShowFatalMessageBox(title, $"{intro}\n\n{topLevelError}\n\nLog: {logPath}");
            return;
        }

        try
        {
            try
            {
                SafeAppend(logPath, $"[Startup] TaskDialog sizes: TASKDIALOGCONFIG={Marshal.SizeOf<TASKDIALOGCONFIG>()}, TASKDIALOG_BUTTON_NATIVE={Marshal.SizeOf<TASKDIALOG_BUTTON_NATIVE>()}, Is64BitProcess={Environment.Is64BitProcess}\n");
            }
            catch
            {
                // ignore
            }

            // Do not show a main icon in the TaskDialog (removes the gem icon at the left).
            var hIcon = IntPtr.Zero;

            var version = TryGetAppVersionString();
            var footer = string.IsNullOrWhiteSpace(version)
                ? $"Log: {logPath}"
                : $"Version: {version}\nLog: {logPath}";

            var buttonsPtr = IntPtr.Zero;
            var buttonTextPtrs = Array.Empty<IntPtr>();
            try
            {
                (buttonsPtr, buttonTextPtrs) = AllocateTaskDialogButtons(new (int Id, string Text)[]
                {
                    (1001, "Open Error Log"),
                    (1002, "Open Log Folder"),
                    (1003, "Email Support (Recommended)"),
                    (1000, "Close"),
                });

                // Keep the dialog open when action buttons are clicked.
                // TaskDialog closes automatically on button press unless we return S_FALSE from TDN_BUTTON_CLICKED.
                TaskDialogCallbackProc? callback = null;
                callback = (hwnd, msg, wParam, lParam, lpRefData) =>
                {
                    try
                    {
                        if (msg != TDN_BUTTON_CLICKED)
                        {
                            return S_OK;
                        }

                        var buttonId = wParam.ToInt32();

                        if (buttonId == 1001)
                        {
                            TryOpenFile(logPath);
                            return S_FALSE;
                        }

                        if (buttonId == 1002)
                        {
                            TryOpenFolder(Path.GetDirectoryName(logPath));
                            return S_FALSE;
                        }

                        if (buttonId == 1003)
                        {
                            TryOpenMailTo(
                                to: "lance@lancelarsen.com",
                                subject: "ScryScreen Error",
                                body: BuildIssueEmailBody(topLevelError, logPath, version));
                            return S_FALSE;
                        }

                        // Close (or any other button): allow dialog to close.
                        return S_OK;
                    }
                    catch
                    {
                        // If something goes wrong, let the dialog close rather than risk being stuck.
                        return S_OK;
                    }
                };

                var config = new TASKDIALOGCONFIG
                {
                    cbSize = (uint)Marshal.SizeOf<TASKDIALOGCONFIG>(),
                    hwndParent = IntPtr.Zero,
                    dwFlags = TASKDIALOG_FLAGS.TDF_ALLOW_DIALOG_CANCELLATION |
                              // Command-links mode has been fragile across some machines; use standard buttons.
                              TASKDIALOG_FLAGS.TDF_NO_ICON,
                    dwCommonButtons = 0,
                    pszWindowTitle = title,
                    pszMainInstruction = "ScryScreen",
                    pszContent = $"{intro}\n\n{topLevelError}\n\nFull details are in the error log.",
                    // Streamlined: no expanded details. Full stack trace is in the log.
                    pszExpandedInformation = null,
                    pszExpandedControlText = null,
                    pszCollapsedControlText = null,
                    pszFooter = footer,
                    cButtons = 4,
                    pButtons = buttonsPtr,
                    nDefaultButton = 1000,
                    hMainIcon = hIcon,
                    pfCallback = Marshal.GetFunctionPointerForDelegate(callback),
                    lpCallbackData = IntPtr.Zero,
                };

                var result = TaskDialogIndirect(ref config, out var pressed, out _, out _);
                GC.KeepAlive(callback);
                if (result != 0)
                {
                    try
                    {
                        var win32 = Marshal.GetLastWin32Error();
                        SafeAppend(logPath, $"[Startup] TaskDialogIndirect failed: HRESULT=0x{result:X8}, Win32={win32}\n");
                    }
                    catch
                    {
                        // ignore
                    }

                    // Fallback if TaskDialog isn't available.
                    TryShowFatalMessageBox(title, $"{intro}\n\n{topLevelError}\n\nLog: {logPath}");
                    return;
                }
            }
            finally
            {
                FreeTaskDialogButtons(buttonsPtr, buttonTextPtrs);

                if (hIcon != IntPtr.Zero)
                {
                    try { DestroyIcon(hIcon); } catch { /* ignore */ }
                }
            }
        }
        catch
        {
            // Last-resort fallback.
            TryShowFatalMessageBox(title, $"{intro}\n\n{topLevelError}\n\nLog: {logPath}");
        }
    }

    private static string BuildIssueEmailBody(string topLevelError, string logPath, string? version)
    {
        var v = string.IsNullOrWhiteSpace(version) ? "(unknown)" : version;
        return
            $"ScryScreen {v} experienced an error.\n" +
            $"Error Details: {topLevelError}\n\n" +
            $"Please Attach the Log File: {logPath}\n" +
            "Thank You!";
    }

    private static (IntPtr Buttons, IntPtr[] ButtonTextPtrs) AllocateTaskDialogButtons((int Id, string Text)[] buttons)
    {
        if (buttons.Length == 0)
        {
            return (IntPtr.Zero, Array.Empty<IntPtr>());
        }

        var size = Marshal.SizeOf<TASKDIALOG_BUTTON_NATIVE>();
        var block = Marshal.AllocHGlobal(size * buttons.Length);
        var textPtrs = new IntPtr[buttons.Length];

        for (var i = 0; i < buttons.Length; i++)
        {
            textPtrs[i] = Marshal.StringToHGlobalUni(buttons[i].Text);
            var native = new TASKDIALOG_BUTTON_NATIVE
            {
                nButtonID = buttons[i].Id,
                pszButtonText = textPtrs[i],
            };

            Marshal.StructureToPtr(native, block + (i * size), fDeleteOld: false);
        }

        return (block, textPtrs);
    }

    private static void FreeTaskDialogButtons(IntPtr buttonsPtr, IntPtr[] buttonTextPtrs)
    {
        try
        {
            if (buttonTextPtrs is not null)
            {
                foreach (var p in buttonTextPtrs)
                {
                    if (p != IntPtr.Zero)
                    {
                        try { Marshal.FreeHGlobal(p); } catch { /* ignore */ }
                    }
                }
            }
        }
        catch
        {
            // ignore
        }

        try
        {
            if (buttonsPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(buttonsPtr);
            }
        }
        catch
        {
            // ignore
        }
    }

    private static string? TryGetAppVersionString()
    {
        try
        {
            var asm = Assembly.GetEntryAssembly();
            if (asm is null)
            {
                return null;
            }

            var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(info))
            {
                return info;
            }

            return asm.GetName().Version?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static IntPtr TryExtractAppIcon(string? exePath)
    {
        try
        {
            // Prefer the branded logo icon if present in the deployed output.
            var baseDir = AppContext.BaseDirectory;
            var candidates = new[]
            {
                Path.Combine(baseDir, "Assets", "scryscreen.ico"),
                Path.Combine(baseDir, "scryscreen.ico"),
            };

            foreach (var path in candidates)
            {
                var icon = TryExtractIconFromFile(path);
                if (icon != IntPtr.Zero)
                {
                    return icon;
                }
            }

            // Fallback: use whatever icon is embedded in the EXE.
            return TryExtractIconFromFile(exePath);
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    private static IntPtr TryExtractIconFromFile(string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return IntPtr.Zero;
            }

            var large = new IntPtr[1];
            var count = ExtractIconExW(path, 0, large, null, 1);
            if (count > 0 && large[0] != IntPtr.Zero)
            {
                return large[0];
            }

            return IntPtr.Zero;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    private static void TryOpenFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true,
                });
            }
        }
        catch
        {
            // ignore
        }
    }

    private static void TryOpenFolder(string? folderPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                return;
            }

            if (Directory.Exists(folderPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = folderPath,
                    UseShellExecute = true,
                });
            }
        }
        catch
        {
            // ignore
        }
    }

    private static void TryOpenMailTo(string to, string subject, string body)
    {
        try
        {
            var uri = BuildMailToUri(to, subject, body);
            Process.Start(new ProcessStartInfo
            {
                FileName = uri,
                UseShellExecute = true,
            });
        }
        catch
        {
            // ignore
        }
    }

    private static string BuildMailToUri(string to, string subject, string body)
    {
        static string E(string s) => Uri.EscapeDataString(s ?? string.Empty);
        return $"mailto:{to}?subject={E(subject)}&body={E(body)}";
    }

    private static bool TryCopyToClipboard(string text)
    {
        try
        {
            if (!OpenClipboard(IntPtr.Zero))
            {
                return false;
            }

            try
            {
                if (!EmptyClipboard())
                {
                    return false;
                }

                // Allocate global memory for the UTF-16 string (including null terminator).
                var bytes = (text.Length + 1) * 2;
                var hGlobal = GlobalAlloc(GmemMoveable, (UIntPtr)bytes);
                if (hGlobal == IntPtr.Zero)
                {
                    return false;
                }

                var target = GlobalLock(hGlobal);
                if (target == IntPtr.Zero)
                {
                    return false;
                }

                try
                {
                    Marshal.Copy(text.ToCharArray(), 0, target, text.Length);
                    Marshal.WriteInt16(target, text.Length * 2, 0);
                }
                finally
                {
                    GlobalUnlock(hGlobal);
                }

                return SetClipboardData(CfUnicodeText, hGlobal) != IntPtr.Zero;
            }
            finally
            {
                CloseClipboard();
            }
        }
        catch
        {
            return false;
        }
    }

    private static void SafeAppend(string logPath, string text)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            // Prefer a shared append so other processes (and readers) can access the log.
            using var stream = new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            using var writer = new StreamWriter(stream);
            writer.Write(text);
        }
        catch
        {
            // ignore
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TASKDIALOG_BUTTON_NATIVE
    {
        public int nButtonID;
        public IntPtr pszButtonText;
    }

    [Flags]
    private enum TASKDIALOG_COMMON_BUTTON_FLAGS : uint
    {
        TDCBF_OK_BUTTON = 0x0001,
    }

    [Flags]
    private enum TASKDIALOG_FLAGS : uint
    {
        TDF_ALLOW_DIALOG_CANCELLATION = 0x0008,
        TDF_USE_COMMAND_LINKS = 0x0010,
        TDF_USE_HICON_MAIN = 0x0002,
        TDF_NO_ICON = 0x0004,
    }

    // comctl32 TaskDialog uses 4-byte packing even on x64; incorrect packing yields E_INVALIDARG.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 4)]
    private struct TASKDIALOGCONFIG
    {
        public uint cbSize;
        public IntPtr hwndParent;
        public IntPtr hInstance;
        public TASKDIALOG_FLAGS dwFlags;
        public TASKDIALOG_COMMON_BUTTON_FLAGS dwCommonButtons;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string pszWindowTitle;

        // union (icons). We only use hMainIcon.
        public IntPtr hMainIcon;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string pszMainInstruction;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string pszContent;

        public uint cButtons;
        public IntPtr pButtons;
        public int nDefaultButton;

        public uint cRadioButtons;
        public IntPtr pRadioButtons;
        public int nDefaultRadioButton;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? pszVerificationText;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? pszExpandedInformation;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? pszExpandedControlText;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? pszCollapsedControlText;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? pszFooter;

        public IntPtr hFooterIcon;

        public IntPtr pfCallback;
        public IntPtr lpCallbackData;
        public uint cxWidth;
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        IconProvider.Current
            .Register<MaterialDesignIconProvider>();

        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
    }
}
