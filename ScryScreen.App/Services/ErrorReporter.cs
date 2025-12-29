using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using ScryScreen.App.Views;

namespace ScryScreen.App.Services;

public static class ErrorReporter
{
    private static Window? _owner;
    private static int _isShowing;

    public static void Initialize(Window owner)
    {
        _owner = owner;

        // UI thread exceptions (most important for Avalonia apps).
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            Report(e.Exception, "UI thread unhandled exception");

            // Prevent the default hard crash so the dialog can be shown.
            // Note: the app may be in a bad state after this.
            e.Handled = true;
        };

        // Background Task exceptions that go unobserved.
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Report(e.Exception, "Unobserved Task exception");
            e.SetObserved();
        };

        // Last-resort handler (may fire on non-UI threads).
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                Report(ex, "AppDomain unhandled exception");
            }
            else
            {
                Trace.WriteLine($"[UnhandledException] {e.ExceptionObject}");
            }
        };
    }

    public static void Report(Exception exception, string? context = null)
    {
        try
        {
            Trace.WriteLine($"[ErrorReporter] {context}\n{exception}");
        }
        catch
        {
            // ignored
        }

        // Avoid recursive/crashing loops if the dialog itself throws.
        if (Interlocked.Exchange(ref _isShowing, 1) == 1)
        {
            return;
        }

        Dispatcher.UIThread.Post(() => _ = ShowDialogAsync(exception, context));
    }

    private static async Task ShowDialogAsync(Exception exception, string? context)
    {
        try
        {
            if (_owner is null)
            {
                return;
            }

            var dialog = new ErrorDialog(exception, context);
            await dialog.ShowDialog(_owner);
        }
        catch
        {
            // ignored
        }
        finally
        {
            Interlocked.Exchange(ref _isShowing, 0);
        }
    }
}
