using System;
using System.Threading;
using System.Threading.Tasks;

namespace ScryScreen.App.Services;

public sealed class VideoPausedFramePrimer
{
    private readonly IVideoDelay _delay;
    private int _isPriming;

    public VideoPausedFramePrimer(IVideoDelay delay)
    {
        _delay = delay ?? throw new ArgumentNullException(nameof(delay));
    }

    public bool IsPriming => Volatile.Read(ref _isPriming) == 1;

    public async Task<bool> PrimePausedFrameAsync(
        IVideoPlayback player,
        long targetMs,
        Func<bool>? isNativeTargetReady = null,
        int decodeDelayMs = 120,
        CancellationToken cancellationToken = default)
    {
        if (player is null)
        {
            throw new ArgumentNullException(nameof(player));
        }

        if (decodeDelayMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(decodeDelayMs));
        }

        if (isNativeTargetReady is not null && !isNativeTargetReady())
        {
            return false;
        }

        if (player.IsPlaying)
        {
            return false;
        }

        if (Interlocked.Exchange(ref _isPriming, 1) == 1)
        {
            return false;
        }

        var originalMute = false;
        var originalVolume = 0;

        try
        {
            originalMute = player.Mute;
            originalVolume = player.Volume;
        }
        catch
        {
            // ignore
        }

        try
        {
            player.Mute = true;

            if (targetMs < 0)
            {
                targetMs = 0;
            }

            player.TimeMs = targetMs;

            player.TryPlay();

            if (decodeDelayMs > 0)
            {
                await _delay.Delay(decodeDelayMs, cancellationToken).ConfigureAwait(false);
            }

            player.Pause();
            player.TimeMs = targetMs;

            return true;
        }
        finally
        {
            try
            {
                player.Mute = originalMute;
                player.Volume = originalVolume;
            }
            catch
            {
                // ignore
            }

            Interlocked.Exchange(ref _isPriming, 0);
        }
    }
}
