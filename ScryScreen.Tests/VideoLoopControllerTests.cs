using System;
using ScryScreen.App.Services;
using Xunit;

namespace ScryScreen.Tests;

public class VideoLoopControllerTests
{
    private sealed class FakeClock
    {
        public long NowTicks;
        public long UtcNowTicks() => NowTicks;
        public void AdvanceMs(int ms) => NowTicks += TimeSpan.FromMilliseconds(ms).Ticks;
    }

    [Fact]
    public void SignalEndReached_DoesNothing_WhenNotEnabled()
    {
        var clock = new FakeClock();
        var restartCalls = 0;
        var controller = new VideoLoopController(
            restart: () => { restartCalls++; return true; },
            utcNowTicks: clock.UtcNowTicks);

        controller.SetHasVideo(true);
        controller.SetArmed(true);
        controller.SetEnabled(false);

        controller.SignalEndReached();
        controller.Tick();

        Assert.Equal(0, restartCalls);
    }

    [Fact]
    public void SignalEndReached_DoesNothing_WhenNotArmed()
    {
        var clock = new FakeClock();
        var restartCalls = 0;
        var controller = new VideoLoopController(
            restart: () => { restartCalls++; return true; },
            utcNowTicks: clock.UtcNowTicks);

        controller.SetHasVideo(true);
        controller.SetEnabled(true);
        controller.SetArmed(false);

        controller.SignalEndReached();
        controller.Tick();

        Assert.Equal(0, restartCalls);
    }

    [Fact]
    public void EndReached_RequestsRestart_AndTickRunsRestartOnce()
    {
        var clock = new FakeClock();
        var restartCalls = 0;
        var controller = new VideoLoopController(
            restart: () => { restartCalls++; return true; },
            utcNowTicks: clock.UtcNowTicks);

        controller.SetHasVideo(true);
        controller.SetEnabled(true);
        controller.SetArmed(true);

        controller.SignalEndReached();
        controller.Tick();
        controller.Tick();

        Assert.Equal(1, restartCalls);
    }

    [Fact]
    public void FailedRestart_UsesBackoff_AndRetriesLater()
    {
        var clock = new FakeClock();
        var restartCalls = 0;
        var succeedOn = 2;

        var controller = new VideoLoopController(
            restart: () =>
            {
                restartCalls++;
                return restartCalls >= succeedOn;
            },
            utcNowTicks: clock.UtcNowTicks);

        controller.SetHasVideo(true);
        controller.SetEnabled(true);
        controller.SetArmed(true);

        controller.SignalEndReached();

        // First attempt fails
        controller.Tick();
        Assert.Equal(1, restartCalls);

        // Immediate ticks should not retry due to backoff
        controller.Tick();
        controller.Tick();
        Assert.Equal(1, restartCalls);

        // Advance past first backoff (350ms)
        clock.AdvanceMs(400);
        controller.Tick();
        Assert.Equal(2, restartCalls);
    }

    [Fact]
    public void NativeTargetNotReady_DefersRestart()
    {
        var clock = new FakeClock();
        var restartCalls = 0;
        var nativeReady = false;

        var controller = new VideoLoopController(
            restart: () => { restartCalls++; return true; },
            utcNowTicks: clock.UtcNowTicks,
            isNativeTargetReady: () => nativeReady);

        controller.SetHasVideo(true);
        controller.SetEnabled(true);
        controller.SetArmed(true);

        controller.SignalEndReached();
        controller.Tick();

        Assert.Equal(0, restartCalls);

        // Even if we tick a bunch, it should still be deferred until time advances.
        controller.Tick();
        controller.Tick();
        Assert.Equal(0, restartCalls);

        nativeReady = true;
        clock.AdvanceMs(300);
        controller.Tick();

        Assert.Equal(1, restartCalls);
    }

    [Fact]
    public void EndReachedIsSuppressedDuringRestart()
    {
        var clock = new FakeClock();
        var restartCalls = 0;
        VideoLoopController? controller = null;

        controller = new VideoLoopController(
            restart: () =>
            {
                restartCalls++;
                // Simulate a re-entrant EndReached occurring during restart.
                controller!.SignalEndReached();
                return true;
            },
            utcNowTicks: clock.UtcNowTicks);

        controller.SetHasVideo(true);
        controller.SetEnabled(true);
        controller.SetArmed(true);

        controller.SignalEndReached();
        controller.Tick();
        controller.Tick();

        Assert.Equal(1, restartCalls);
    }
}
