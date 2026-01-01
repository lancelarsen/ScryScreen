using Avalonia;
using System;
using System.Diagnostics;
using System.IO;
using LibVLCSharp.Shared;
using Projektanker.Icons.Avalonia;
using Projektanker.Icons.Avalonia.MaterialDesign;

namespace ScryScreen.App;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        var logPath = GetLogPath();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            if (File.Exists(logPath))
            {
                File.Delete(logPath);
            }

            Trace.Listeners.Add(new TextWriterTraceListener(logPath));
            Trace.AutoFlush = true;

            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                Trace.WriteLine($"[UnhandledException] {e.ExceptionObject}");

            // LibVLCSharp: loads native VLC runtime (Windows MVP)
            LibVLCSharp.Shared.Core.Initialize();

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                File.AppendAllText(logPath, $"[Fatal] {ex}\n");
            }
            catch
            {
                // ignored
            }

            throw;
        }
    }

    private static string GetLogPath()
        => Path.Combine(AppContext.BaseDirectory, "ScryScreen.startup.log");

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
