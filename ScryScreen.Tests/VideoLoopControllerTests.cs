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

    [Fact]
    public void DisablingController_ClearsPendingRestart()
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
        controller.SetEnabled(false);

        controller.SetEnabled(true);
        controller.Tick();

        Assert.Equal(0, restartCalls);
    }

    [Fact]
    public void ClearingHasVideo_ClearsPendingRestart()
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
        controller.SetHasVideo(false);

        controller.SetHasVideo(true);
        controller.Tick();

        Assert.Equal(0, restartCalls);
    }

    [Fact]
    public void ClearingArmed_ClearsPendingRestart()
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
        controller.SetArmed(false);

        controller.SetArmed(true);
        controller.Tick();

        Assert.Equal(0, restartCalls);
    }

    [Fact]
    public void RestartExceptions_AreCaught_AndBackoffIsApplied()
    {
        var clock = new FakeClock();
        var restartCalls = 0;

        var controller = new VideoLoopController(
            restart: () =>
            {
                restartCalls++;
                throw new InvalidOperationException("boom");
            },
            utcNowTicks: clock.UtcNowTicks);

        controller.SetHasVideo(true);
        controller.SetEnabled(true);
        controller.SetArmed(true);
        controller.SignalEndReached();

        controller.Tick();
        Assert.Equal(1, restartCalls);

        controller.Tick();
        Assert.Equal(1, restartCalls);

        clock.AdvanceMs(400);
        controller.Tick();
        Assert.Equal(2, restartCalls);
    }

    [Fact]
    public void Backoff_ClampsAt1200ms_AfterRepeatedFailures()
    {
        var clock = new FakeClock();
        var restartCalls = 0;

        var controller = new VideoLoopController(
            restart: () => { restartCalls++; return false; },
            utcNowTicks: clock.UtcNowTicks);

        controller.SetHasVideo(true);
        controller.SetEnabled(true);
        controller.SetArmed(true);
        controller.SignalEndReached();

        controller.Tick(); // attempt 1, sets 350ms
        Assert.Equal(1, restartCalls);
        clock.AdvanceMs(400);
        controller.Tick(); // attempt 2, sets 500ms
        Assert.Equal(2, restartCalls);
        clock.AdvanceMs(600);
        controller.Tick(); // attempt 3, sets 800ms
        Assert.Equal(3, restartCalls);
        clock.AdvanceMs(900);
        controller.Tick(); // attempt 4, sets 1200ms (clamped)
        Assert.Equal(4, restartCalls);

        // Not enough time to hit 1200ms yet.
        clock.AdvanceMs(1000);
        controller.Tick();
        Assert.Equal(4, restartCalls);

        clock.AdvanceMs(300);
        controller.Tick();
        Assert.Equal(5, restartCalls);
    }
}
