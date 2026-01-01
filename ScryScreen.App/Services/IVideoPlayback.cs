using System;

namespace ScryScreen.App.Services;

public interface IVideoPlayback
{
    bool IsPlaying { get; }

    long TimeMs { get; set; }

    bool Mute { get; set; }

    int Volume { get; set; }

    bool TryPlay();

    void Pause();
}
