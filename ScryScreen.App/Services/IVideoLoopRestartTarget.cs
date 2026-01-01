namespace ScryScreen.App.Services;

public interface IVideoLoopRestartTarget<TMedia>
{
    bool IsNativeTargetReady { get; }

    TMedia? Media { get; set; }

    bool TryStop();

    bool TryPlay();

    bool TrySetTimeMs(long timeMs);
}
