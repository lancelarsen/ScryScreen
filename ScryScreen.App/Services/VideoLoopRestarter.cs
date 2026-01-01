using System;

namespace ScryScreen.App.Services;

public sealed class VideoLoopRestarter<TMedia>
{
    private readonly IVideoLoopRestartTarget<TMedia> _target;
    private readonly IVideoMediaFactory<TMedia> _mediaFactory;
    private readonly IVideoSleeper _sleeper;

    public VideoLoopRestarter(
        IVideoLoopRestartTarget<TMedia> target,
        IVideoMediaFactory<TMedia> mediaFactory,
        IVideoSleeper sleeper)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _mediaFactory = mediaFactory ?? throw new ArgumentNullException(nameof(mediaFactory));
        _sleeper = sleeper ?? throw new ArgumentNullException(nameof(sleeper));
    }

    public bool TryRestart(ref TMedia? currentMedia, string? mediaPath)
    {
        if (!_target.IsNativeTargetReady)
        {
            return false;
        }

        // Step 1: Stop to reset "ended" state reliably.
        _target.TryStop();
        _sleeper.Sleep(25);

        // Step 2: Force start position.
        _target.TrySetTimeMs(0);
        _sleeper.Sleep(25);

        // Step 3: Try to play using the currently assigned media.
        if (_target.TryPlay())
        {
            return true;
        }

        // Step 4: Re-assign the tracked media and retry.
        if (currentMedia is not null)
        {
            try
            {
                _target.Media = currentMedia;
            }
            catch
            {
                // ignore
            }

            if (_target.TryPlay())
            {
                return true;
            }
        }

        // Step 5: As a last resort, recreate the media instance.
        if (!string.IsNullOrWhiteSpace(mediaPath))
        {
            var oldMedia = currentMedia;
            var newMedia = _mediaFactory.CreateFromPath(mediaPath);

            currentMedia = newMedia;
            try
            {
                _target.Media = newMedia;
            }
            catch
            {
                // ignore
            }

            if (oldMedia is not null)
            {
                try
                {
                    _mediaFactory.Dispose(oldMedia);
                }
                catch
                {
                    // ignore
                }
            }

            if (_target.TryPlay())
            {
                return true;
            }
        }

        return false;
    }
}
