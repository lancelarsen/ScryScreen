using System;

namespace ScryScreen.Core.Utilities;

public static class TimeFormatter
{
    public static string FormatMs(long ms)
    {
        if (ms < 0) ms = 0;

        var ts = TimeSpan.FromMilliseconds(ms);
        if (ts.TotalHours >= 1)
        {
            return ts.ToString("h\\:mm\\:ss");
        }

        return ts.ToString("m\\:ss");
    }
}
