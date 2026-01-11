using System;
using System.Diagnostics;

namespace ScryScreen.App.Utilities;

public static class UrlLauncher
{
    public static void TryOpen(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch
        {
            // Best-effort fallback for non-Windows platforms.
            try
            {
                if (OperatingSystem.IsLinux())
                {
                    Process.Start("xdg-open", url);
                }
                else if (OperatingSystem.IsMacOS())
                {
                    Process.Start("open", url);
                }
            }
            catch
            {
                // ignore
            }
        }
    }
}
