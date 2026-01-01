using System;
using System.Threading;

namespace ScryScreen.App.Services;

public sealed class VideoLoopController
{
    private readonly Func<bool> _restart;
    private readonly Func<long> _utcNowTicks;
    private readonly Func<bool>? _isNativeTargetReady;

    private volatile bool _enabled;
    private volatile bool _hasVideo;
    private volatile bool _armed;

    private int _restartRequested;
    private int _isRestarting;
    private int _suppressEndReached;

    private long _nextRestartAtTicks;
    private int _restartAttempts;

    public VideoLoopController(Func<bool> restart, Func<long> utcNowTicks, Func<bool>? isNativeTargetReady = null)
    {
        _restart = restart ?? throw new ArgumentNullException(nameof(restart));
        _utcNowTicks = utcNowTicks ?? throw new ArgumentNullException(nameof(utcNowTicks));
        _isNativeTargetReady = isNativeTargetReady;

        _enabled = false;
        _hasVideo = false;
        _armed = false;
        _restartRequested = 0;
        _isRestarting = 0;
        _suppressEndReached = 0;
        _nextRestartAtTicks = 0;
        _restartAttempts = 0;
    }

    public bool IsEndReachedSuppressed => Volatile.Read(ref _suppressEndReached) == 1;

    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        if (!enabled)
        {
            ResetPending();
        }
    }

    public void SetHasVideo(bool hasVideo)
    {
        _hasVideo = hasVideo;
        if (!hasVideo)
        {
            ResetPending();
        }
    }

    public void SetArmed(bool armed)
    {
        _armed = armed;
        if (!armed)
        {
            // If the user isn't actively playing, do not keep a pending loop restart queued.
            ResetPending();
        }
    }

    public void SignalEndReached()
    {
        if (!_enabled || !_hasVideo || !_armed)
        {
            return;
        }

        if (IsEndReachedSuppressed)
        {
            return;
        }

        Interlocked.Exchange(ref _restartRequested, 1);
        Interlocked.Exchange(ref _nextRestartAtTicks, _utcNowTicks());
    }

    public void Tick()
    {
        if (!_enabled || !_hasVideo)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _restartRequested, 0, 0) == 0)
        {
            return;
        }

        var now = _utcNowTicks();
        var nextAt = Interlocked.Read(ref _nextRestartAtTicks);
        if (nextAt != 0 && now < nextAt)
        {
            return;
        }

        if (!_armed)
        {
            ResetPending();
            return;
        }

        // If we don't have a native target yet, avoid triggering VLC fallback windows.
        if (_isNativeTargetReady is not null && !_isNativeTargetReady())
        {
            Interlocked.Exchange(ref _nextRestartAtTicks, now + TimeSpan.FromMilliseconds(250).Ticks);
            return;
        }

        if (Interlocked.Exchange(ref _isRestarting, 1) == 1)
        {
            return;
        }

        try
        {
            Interlocked.Exchange(ref _suppressEndReached, 1);
            var ok = false;
            try
            {
                ok = _restart();
            }
            catch
            {
                ok = false;
            }

            if (ok)
            {
                ResetPending();
                return;
            }

            _restartAttempts++;
            var delayMs = ComputeBackoffMs(_restartAttempts);
            Interlocked.Exchange(ref _nextRestartAtTicks, now + TimeSpan.FromMilliseconds(delayMs).Ticks);
        }
        finally
        {
            Interlocked.Exchange(ref _suppressEndReached, 0);
            Interlocked.Exchange(ref _isRestarting, 0);
        }
    }

    public void ResetPending()
    {
        Interlocked.Exchange(ref _restartRequested, 0);
        Interlocked.Exchange(ref _nextRestartAtTicks, 0);
        _restartAttempts = 0;
    }

    private static int ComputeBackoffMs(int attempts)
    {
        // attempts=1 => 350ms
        // attempts=2 => 500ms
        // attempts=3 => 800ms
        // attempts>=4 => clamp
        var capped = Math.Min(4, Math.Max(1, attempts));
        return capped switch
        {
            1 => 350,
            2 => 500,
            3 => 800,
            _ => 1200,
        };
    }
}
