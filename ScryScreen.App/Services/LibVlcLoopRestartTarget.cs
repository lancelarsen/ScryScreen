using System;
using LibVLCSharp.Shared;

namespace ScryScreen.App.Services;

public sealed class LibVlcLoopRestartTarget : IVideoLoopRestartTarget<Media>
{
    private readonly MediaPlayer _player;
    private readonly Func<bool> _isNativeTargetReady;

    public LibVlcLoopRestartTarget(MediaPlayer player, Func<bool> isNativeTargetReady)
    {
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _isNativeTargetReady = isNativeTargetReady ?? throw new ArgumentNullException(nameof(isNativeTargetReady));
    }

    public bool IsNativeTargetReady
    {
        get
        {
            try
            {
                return _isNativeTargetReady();
            }
            catch
            {
                return false;
            }
        }
    }

    public Media? Media
    {
        get
        {
            try { return _player.Media; }
            catch { return null; }
        }
        set
        {
            try { _player.Media = value; }
            catch { /* ignore */ }
        }
    }

    public bool TryStop()
    {
        try
        {
            _player.Stop();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool TryPlay()
    {
        try
        {
            return _player.Play();
        }
        catch
        {
            return false;
        }
    }

    public bool TrySetTimeMs(long timeMs)
    {
        try
        {
            _player.Time = timeMs;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
