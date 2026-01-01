using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ScryScreen.App.Services;
using Xunit;

namespace ScryScreen.Tests;

public class VideoPausedFramePrimerTests
{
    private sealed class FakePlayback : IVideoPlayback
    {
        public readonly List<string> Calls = new();

        public bool IsPlaying { get; set; }

        private long _timeMs;
        public long TimeMs
        {
            get => _timeMs;
            set
            {
                _timeMs = value;
                Calls.Add($"Time={value}");
            }
        }

        private bool _mute;
        public bool Mute
        {
            get => _mute;
            set
            {
                _mute = value;
                Calls.Add($"Mute={value}");
            }
        }

        private int _volume;
        public int Volume
        {
            get => _volume;
            set
            {
                _volume = value;
                Calls.Add($"Volume={value}");
            }
        }

        public void SetInitialState(bool mute, int volume, long timeMs = 0)
        {
            _mute = mute;
            _volume = volume;
            _timeMs = timeMs;
        }

        public bool TryPlay()
        {
            Calls.Add("Play");
            return true;
        }

        public void Pause()
        {
            Calls.Add("Pause");
        }
    }

    private sealed class ImmediateDelay : IVideoDelay
    {
        public int Calls;
        public Task Delay(int milliseconds, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingDelay : IVideoDelay
    {
        private readonly TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;

        public void Release() => _tcs.TrySetResult();

        public async Task Delay(int milliseconds, CancellationToken cancellationToken = default)
        {
            _started.TrySetResult();
            using var reg = cancellationToken.Register(() => _tcs.TrySetCanceled(cancellationToken));
            await _tcs.Task.ConfigureAwait(false);
        }
    }

    [Fact]
    public async Task PrimePausedFrameAsync_ReturnsFalse_WhenNativeTargetNotReady()
    {
        var delay = new ImmediateDelay();
        var primer = new VideoPausedFramePrimer(delay);
        var player = new FakePlayback { IsPlaying = false };
        player.SetInitialState(mute: false, volume: 80);

        var ok = await primer.PrimePausedFrameAsync(
            player,
            targetMs: 123,
            isNativeTargetReady: () => false,
            decodeDelayMs: 0);

        Assert.False(ok);
        Assert.Empty(player.Calls);
        Assert.Equal(0, delay.Calls);
    }

    [Fact]
    public async Task PrimePausedFrameAsync_ReturnsFalse_WhenPlayerIsPlaying()
    {
        var delay = new ImmediateDelay();
        var primer = new VideoPausedFramePrimer(delay);
        var player = new FakePlayback { IsPlaying = true };
        player.SetInitialState(mute: false, volume: 80);

        var ok = await primer.PrimePausedFrameAsync(player, targetMs: 123, decodeDelayMs: 0);

        Assert.False(ok);
        Assert.Empty(player.Calls);
        Assert.Equal(0, delay.Calls);
    }

    [Fact]
    public async Task PrimePausedFrameAsync_PrimesAndRestoresAudioState()
    {
        var delay = new ImmediateDelay();
        var primer = new VideoPausedFramePrimer(delay);
        var player = new FakePlayback { IsPlaying = false };
        player.SetInitialState(mute: false, volume: 80);

        var ok = await primer.PrimePausedFrameAsync(player, targetMs: 1234, decodeDelayMs: 5);

        Assert.True(ok);

        // Minimal behavioral assertions: mute is enabled during priming, then restored.
        Assert.Contains("Mute=True", player.Calls);
        Assert.False(player.Mute);
        Assert.Equal(80, player.Volume);

        // Expected core sequence: seek -> play -> pause -> seek.
        var joined = string.Join("|", player.Calls);
        Assert.Contains("Time=1234|Play|Pause|Time=1234", joined);

        // Delay called once (even though it's immediate).
        Assert.Equal(1, delay.Calls);
    }

    [Fact]
    public async Task PrimePausedFrameAsync_ClampsNegativeTargetToZero()
    {
        var delay = new ImmediateDelay();
        var primer = new VideoPausedFramePrimer(delay);
        var player = new FakePlayback { IsPlaying = false };
        player.SetInitialState(mute: false, volume: 80);

        var ok = await primer.PrimePausedFrameAsync(player, targetMs: -10, decodeDelayMs: 0);

        Assert.True(ok);
        Assert.Contains("Time=0", player.Calls);

        // Should set time at least twice (before play and after pause).
        Assert.True(player.Calls.FindAll(c => c == "Time=0").Count >= 2);
    }

    [Fact]
    public async Task PrimePausedFrameAsync_IsNonReentrant()
    {
        var delay = new BlockingDelay();
        var primer = new VideoPausedFramePrimer(delay);
        var player = new FakePlayback { IsPlaying = false };
        player.SetInitialState(mute: false, volume: 80);

        var first = primer.PrimePausedFrameAsync(player, targetMs: 500, decodeDelayMs: 1);
        await delay.Started;

        var secondOk = await primer.PrimePausedFrameAsync(player, targetMs: 600, decodeDelayMs: 1);
        Assert.False(secondOk);

        delay.Release();
        var firstOk = await first;
        Assert.True(firstOk);
    }
}
