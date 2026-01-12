using System;

namespace ScryScreen.App.Models;

public readonly record struct HourglassState(
    TimeSpan Duration,
    TimeSpan Remaining,
    bool IsRunning)
{
    public static HourglassState Stopped(TimeSpan duration)
        => new(duration <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) : duration, duration, IsRunning: false);

    public double FractionRemaining
    {
        get
        {
            var d = Duration.TotalMilliseconds;
            if (d <= 0)
            {
                return 0;
            }

            var r = Remaining.TotalMilliseconds;
            if (r <= 0)
            {
                return 0;
            }

            if (r >= d)
            {
                return 1;
            }

            return r / d;
        }
    }
}
