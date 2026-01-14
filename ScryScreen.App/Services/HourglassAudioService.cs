using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia.Platform;
using NAudio.Wave;
using ScryScreen.App.ViewModels;

namespace ScryScreen.App.Services;

internal sealed class HourglassAudioService : IDisposable
{
    private const string AssetBase = "avares://ScryScreen.App/Assets/Sounds/";

    private static readonly string[] SupportedExtensions = [".mp3", ".wav"]; // prefer mp3 when both exist

    private readonly HourglassViewModel _hourglass;

    private readonly Dictionary<string, LoopHandle> _loops = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<OneShotHandle> _oneShots = new();

    private bool _lastIsRunning;
    private TimeSpan _lastRemaining;
    private bool _hasSnapshot;

    public HourglassAudioService(HourglassViewModel hourglass)
    {
        _hourglass = hourglass ?? throw new ArgumentNullException(nameof(hourglass));
        _hourglass.StateChanged += OnHourglassStateChanged;
        _hourglass.PropertyChanged += OnHourglassPropertyChanged;

        SnapshotAndApply();
    }

    public void Dispose()
    {
        _hourglass.StateChanged -= OnHourglassStateChanged;
        _hourglass.PropertyChanged -= OnHourglassPropertyChanged;

        foreach (var kvp in _loops)
        {
            try { kvp.Value.Dispose(); } catch { }
        }

        foreach (var h in _oneShots)
        {
            try { h.Dispose(); } catch { }
        }

        _loops.Clear();
        _oneShots.Clear();
    }

    private void OnHourglassStateChanged()
        => SnapshotAndApply();

    private void OnHourglassPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(HourglassViewModel.PlaySoundsEnabled) or nameof(HourglassViewModel.IsRunning) or nameof(HourglassViewModel.Remaining))
        {
            SnapshotAndApply();
        }
    }

    private void SnapshotAndApply()
    {
        var isRunning = _hourglass.IsRunning;
        var remaining = _hourglass.Remaining;
        var sounds = _hourglass.PlaySoundsEnabled;

        var prevIsRunning = _hasSnapshot && _lastIsRunning;
        var prevRemaining = _hasSnapshot ? _lastRemaining : remaining;

        _lastIsRunning = isRunning;
        _lastRemaining = remaining;
        _hasSnapshot = true;

        if (!sounds)
        {
            StopLoop("hourglass");
            return;
        }

        // Loop while running and time remains.
        if (isRunning && remaining > TimeSpan.Zero)
        {
            // Gentle loop; should sit behind most table audio.
            SetLoop("hourglass", "hourglass_sand_loop", volume: 0.35f);
        }
        else
        {
            StopLoop("hourglass");
        }

        // Out-of-time gong: only when we transition from >0 to ==0.
        if (prevRemaining > TimeSpan.Zero && remaining <= TimeSpan.Zero)
        {
            PlayOneShot("hourglass_gong", volume: 0.95f);
            return;
        }

        // Reset: only when stopped, and Remaining jumps upward noticeably.
        // This avoids firing during ticking or minor adjustments.
        if (!isRunning && prevRemaining + TimeSpan.FromMilliseconds(250) < remaining)
        {
            PlayOneShot("hourglass_reset", volume: 0.80f);
            return;
        }

        _ = prevIsRunning; // reserved for future (start/pause sounds)
    }

    private void SetLoop(string key, string assetBaseName, float volume)
    {
        if (volume <= 0.001f)
        {
            StopLoop(key);
            return;
        }

        if (_loops.TryGetValue(key, out var existing))
        {
            existing.SetVolume(volume);
            return;
        }

        if (!TryCreateLoop(assetBaseName, volume, out var handle))
        {
            return;
        }

        _loops[key] = handle;
    }

    private void StopLoop(string key)
    {
        if (_loops.TryGetValue(key, out var handle))
        {
            _loops.Remove(key);
            try { handle.Dispose(); } catch { }
        }
    }

    private void PlayOneShot(string assetBaseName, float volume)
    {
        if (volume <= 0.001f)
        {
            return;
        }

        try
        {
            if (!TryOpenAsset(assetBaseName, out var assetFileName, out var stream))
            {
                return;
            }

            WaveStream? reader = null;
            WaveChannel32? channel = null;
            WaveOutEvent? output = null;
            OneShotHandle? handle = null;

            try
            {
                reader = CreateReader(assetFileName, stream);
                channel = new WaveChannel32(reader) { PadWithZeroes = false, Volume = volume };
                output = new WaveOutEvent();
                output.Init(channel);

                handle = new OneShotHandle(output, channel, reader, stream);
                _oneShots.Add(handle);

                output.PlaybackStopped += (_, _) =>
                {
                    try
                    {
                        _oneShots.Remove(handle);
                        handle.Dispose();
                    }
                    catch
                    {
                        // ignore
                    }
                };

                output.Play();
            }
            catch
            {
                try { handle?.Dispose(); } catch { }
                try { output?.Dispose(); } catch { }
                try { channel?.Dispose(); } catch { }
                try { reader?.Dispose(); } catch { }
                try { stream.Dispose(); } catch { }
                throw;
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[HourglassAudio] One-shot '{assetBaseName}' failed: {ex.Message}");
        }
    }

    private static bool TryCreateLoop(string assetBaseName, float volume, out LoopHandle handle)
    {
        handle = null!;

        try
        {
            if (!TryOpenAsset(assetBaseName, out var assetFileName, out var stream))
            {
                return false;
            }

            WaveStream? reader = null;
            WaveChannel32? channel = null;
            LoopStream? loop = null;

            try
            {
                reader = CreateReader(assetFileName, stream);
                channel = new WaveChannel32(reader) { PadWithZeroes = false, Volume = volume };
                loop = new LoopStream(channel);

                var output = new WaveOutEvent();
                output.Init(loop);
                output.Play();

                handle = new LoopHandle(output, loop, channel, reader, stream);
                return true;
            }
            catch
            {
                try { loop?.Dispose(); } catch { }
                try { channel?.Dispose(); } catch { }
                try { reader?.Dispose(); } catch { }
                try { stream.Dispose(); } catch { }
                throw;
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[HourglassAudio] Loop '{assetBaseName}' failed: {ex.Message}");
            return false;
        }
    }

    private static bool TryOpenAsset(string assetBaseName, out string assetFileName, out System.IO.Stream stream)
    {
        foreach (var ext in SupportedExtensions)
        {
            var fileName = assetBaseName.EndsWith(ext, StringComparison.OrdinalIgnoreCase)
                ? assetBaseName
                : assetBaseName + ext;

            var uri = new Uri(AssetBase + fileName);
            if (!AssetLoader.Exists(uri))
            {
                continue;
            }

            try
            {
                using var raw = AssetLoader.Open(uri);
                var ms = new System.IO.MemoryStream();
                raw.CopyTo(ms);
                ms.Position = 0;

                assetFileName = fileName;
                stream = ms;
                return true;
            }
            catch
            {
                // Try other extensions if possible.
            }
        }

        assetFileName = string.Empty;
        stream = null!;
        return false;
    }

    private static WaveStream CreateReader(string assetFileName, System.IO.Stream stream)
    {
        if (assetFileName.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
        {
            return new Mp3FileReader(stream);
        }

        return new WaveFileReader(stream);
    }

    private sealed class LoopHandle : IDisposable
    {
        private readonly IWavePlayer _output;
        private readonly WaveStream _loop;
        private readonly WaveChannel32 _channel;
        private readonly WaveStream _reader;
        private readonly System.IO.Stream _stream;

        public LoopHandle(IWavePlayer output, WaveStream loop, WaveChannel32 channel, WaveStream reader, System.IO.Stream stream)
        {
            _output = output;
            _loop = loop;
            _channel = channel;
            _reader = reader;
            _stream = stream;
        }

        public void SetVolume(float v)
            => _channel.Volume = v;

        public void Dispose()
        {
            try { _output.Stop(); } catch { }
            try { _output.Dispose(); } catch { }
            try { _loop.Dispose(); } catch { }
            try { _channel.Dispose(); } catch { }
            try { _reader.Dispose(); } catch { }
            try { _stream.Dispose(); } catch { }
        }
    }

    private sealed class OneShotHandle : IDisposable
    {
        private readonly IWavePlayer _output;
        private readonly WaveChannel32 _channel;
        private readonly WaveStream _reader;
        private readonly System.IO.Stream _stream;

        public OneShotHandle(IWavePlayer output, WaveChannel32 channel, WaveStream reader, System.IO.Stream stream)
        {
            _output = output;
            _channel = channel;
            _reader = reader;
            _stream = stream;
        }

        public void Dispose()
        {
            try { _output.Stop(); } catch { }
            try { _output.Dispose(); } catch { }
            try { _channel.Dispose(); } catch { }
            try { _reader.Dispose(); } catch { }
            try { _stream.Dispose(); } catch { }
        }
    }
}
