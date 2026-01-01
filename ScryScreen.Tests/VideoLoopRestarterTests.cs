using System;
using System.Collections.Generic;
using ScryScreen.App.Services;
using Xunit;

namespace ScryScreen.Tests;

public class VideoLoopRestarterTests
{
    private sealed class FakeSleeper : IVideoSleeper
    {
        public readonly List<int> Sleeps = new();
        public void Sleep(int milliseconds) => Sleeps.Add(milliseconds);
    }

    private sealed class FakeTarget : IVideoLoopRestartTarget<object>
    {
        public bool IsNativeTargetReady { get; set; } = true;

        public object? Media { get; set; }

        public readonly List<string> Calls = new();

        public Queue<bool> StopResults { get; } = new();
        public Queue<bool> PlayResults { get; } = new();
        public Queue<bool> SetTimeResults { get; } = new();

        public bool TryStop()
        {
            Calls.Add("Stop");
            return StopResults.Count > 0 ? StopResults.Dequeue() : true;
        }

        public bool TryPlay()
        {
            Calls.Add("Play");
            return PlayResults.Count > 0 ? PlayResults.Dequeue() : false;
        }

        public bool TrySetTimeMs(long timeMs)
        {
            Calls.Add($"Time={timeMs}");
            return SetTimeResults.Count > 0 ? SetTimeResults.Dequeue() : true;
        }
    }

    private sealed class FakeFactory : IVideoMediaFactory<object>
    {
        public readonly List<string> CreatedFromPaths = new();
        public readonly List<object> Disposed = new();

        public object CreateFromPath(string filePath)
        {
            CreatedFromPaths.Add(filePath);
            return new object();
        }

        public void Dispose(object media)
        {
            Disposed.Add(media);
        }
    }

    [Fact]
    public void TryRestart_ReturnsFalse_WhenNativeTargetNotReady()
    {
        var target = new FakeTarget { IsNativeTargetReady = false };
        var factory = new FakeFactory();
        var sleeper = new FakeSleeper();
        var restarter = new VideoLoopRestarter<object>(target, factory, sleeper);

        object? current = new object();
        var ok = restarter.TryRestart(ref current, mediaPath: "c:/x.mp4");

        Assert.False(ok);
        Assert.Empty(target.Calls);
        Assert.Empty(factory.CreatedFromPaths);
        Assert.Empty(sleeper.Sleeps);
    }

    [Fact]
    public void TryRestart_Succeeds_OnFirstPlayAttempt()
    {
        var target = new FakeTarget();
        target.PlayResults.Enqueue(true);

        var factory = new FakeFactory();
        var sleeper = new FakeSleeper();
        var restarter = new VideoLoopRestarter<object>(target, factory, sleeper);

        object? current = new object();
        var ok = restarter.TryRestart(ref current, mediaPath: "c:/x.mp4");

        Assert.True(ok);
        Assert.Equal(new[] { "Stop", "Time=0", "Play" }, target.Calls);
        Assert.Equal(new[] { 25, 25 }, sleeper.Sleeps);
        Assert.Empty(factory.CreatedFromPaths);
    }

    [Fact]
    public void TryRestart_ReassignsCurrentMedia_ThenPlays()
    {
        var target = new FakeTarget();
        target.PlayResults.Enqueue(false); // first play fails
        target.PlayResults.Enqueue(true);  // second play succeeds after media reassign

        var factory = new FakeFactory();
        var sleeper = new FakeSleeper();
        var restarter = new VideoLoopRestarter<object>(target, factory, sleeper);

        var media = new object();
        object? current = media;

        var ok = restarter.TryRestart(ref current, mediaPath: null);

        Assert.True(ok);
        Assert.Same(media, target.Media);
        Assert.Equal(new[] { "Stop", "Time=0", "Play", "Play" }, target.Calls);
        Assert.Empty(factory.CreatedFromPaths);
    }

    [Fact]
    public void TryRestart_RecreatesMedia_WhenNeeded_AndDisposesOld()
    {
        var target = new FakeTarget();
        target.PlayResults.Enqueue(false); // first play fails
        target.PlayResults.Enqueue(false); // second play fails (after reassign)
        target.PlayResults.Enqueue(true);  // third play succeeds (after recreate)

        var factory = new FakeFactory();
        var sleeper = new FakeSleeper();
        var restarter = new VideoLoopRestarter<object>(target, factory, sleeper);

        var oldMedia = new object();
        object? current = oldMedia;

        var ok = restarter.TryRestart(ref current, mediaPath: "c:/video.mp4");

        Assert.True(ok);
        Assert.Single(factory.CreatedFromPaths);
        Assert.Equal("c:/video.mp4", factory.CreatedFromPaths[0]);
        Assert.Single(factory.Disposed);
        Assert.Same(oldMedia, factory.Disposed[0]);
        Assert.NotNull(current);
        Assert.NotSame(oldMedia, current);
    }

    [Fact]
    public void TryRestart_DoesNotRecreateMedia_WhenPathMissing()
    {
        var target = new FakeTarget();
        target.PlayResults.Enqueue(false);
        target.PlayResults.Enqueue(false);

        var factory = new FakeFactory();
        var sleeper = new FakeSleeper();
        var restarter = new VideoLoopRestarter<object>(target, factory, sleeper);

        object? current = new object();
        var ok = restarter.TryRestart(ref current, mediaPath: null);

        Assert.False(ok);
        Assert.Empty(factory.CreatedFromPaths);
    }
}
