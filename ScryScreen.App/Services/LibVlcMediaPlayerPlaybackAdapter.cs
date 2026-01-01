using System;
using LibVLCSharp.Shared;

namespace ScryScreen.App.Services;

public sealed class LibVlcMediaPlayerPlaybackAdapter : IVideoPlayback
{
    private readonly MediaPlayer _player;

    public LibVlcMediaPlayerPlaybackAdapter(MediaPlayer player)
    {
        _player = player ?? throw new ArgumentNullException(nameof(player));
    }

    public bool IsPlaying
    {
        get
        {
            try { return _player.IsPlaying; }
            catch { return false; }
        }
    }

    public long TimeMs
    {
        get
        {
            try { return _player.Time; }
            catch { return 0; }
        }
        set
        {
            try { _player.Time = value; }
            catch { /* ignore */ }
        }
    }

    public bool Mute
    {
        get
        {
            try { return _player.Mute; }
            catch { return false; }
        }
        set
        {
            try { _player.Mute = value; }
            catch { /* ignore */ }
        }
    }

    public int Volume
    {
        get
        {
            try { return _player.Volume; }
            catch { return 0; }
        }
        set
        {
            try { _player.Volume = value; }
            catch { /* ignore */ }
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

    public void Pause()
    {
        try
        {
            _player.Pause();
        }
        catch
        {
            // ignore
        }
    }
}
