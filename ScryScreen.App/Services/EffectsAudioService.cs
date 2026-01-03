using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Avalonia.Platform;
using NAudio.Wave;

namespace ScryScreen.App.Services;

internal sealed class EffectsAudioService : IDisposable
{
    private const string AssetBase = "avares://ScryScreen.App/Assets/Sounds/";

    private static readonly string[] SupportedExtensions = [".mp3", ".wav"]; // prefer mp3 when both exist

    private readonly Dictionary<string, LoopHandle> _loops = new(StringComparer.OrdinalIgnoreCase);

    private readonly List<OneShotHandle> _oneShots = new();

    private readonly Dictionary<int, PortalEffectsSnapshot> _latestPortals = new();

    private DateTime _lastThunderUtc = DateTime.MinValue;

    public readonly record struct PortalEffectsSnapshot(int PortalNumber, bool IsVisible, Models.OverlayEffectsState Effects);

    public EffectsAudioService()
    {
        EffectsEventBus.LightningFlash += OnLightningFlash;
        EffectsEventBus.QuakeStarted += OnQuakeStarted;
        // Quake audio is handled as a one-shot on QuakeStarted.
    }

    public void Dispose()
    {
        EffectsEventBus.LightningFlash -= OnLightningFlash;
        EffectsEventBus.QuakeStarted -= OnQuakeStarted;

        foreach (var kvp in _loops.ToArray())
        {
            try
            {
                kvp.Value.Dispose();
            }
            catch
            {
                // ignore
            }
        }

        foreach (var h in _oneShots.ToArray())
        {
            try
            {
                h.Dispose();
            }
            catch
            {
                // ignore
            }
        }

        _loops.Clear();
        _oneShots.Clear();
        _latestPortals.Clear();
    }

    public void UpdateFromPortalEffects(IEnumerable<PortalEffectsSnapshot> portals)
    {
        var list = portals?.ToList() ?? new List<PortalEffectsSnapshot>();

        _latestPortals.Clear();
        foreach (var p in list)
        {
            _latestPortals[p.PortalNumber] = p;
        }

        // Loops
        SetLoop("rain", "rain_loop", VolumeForLoop(MaxIntensity(list, s => s.RainEnabled && s.RainSoundEnabled, s => s.RainIntensity), maxVolume: 0.55));
        SetLoop("sand", "sand_wind_loop", VolumeForLoop(MaxIntensity(list, s => s.SandEnabled && s.SandSoundEnabled, s => s.SandIntensity), maxVolume: 0.55));
        SetLoop("fire", "fire_crackle_loop", VolumeForLoop(MaxIntensity(list, s => s.FireEnabled && s.FireSoundEnabled, s => s.FireIntensity), maxVolume: 0.70));
    }

    public void PlayLightningThunder(double intensity)
    {
        PlayOneShot("thunder_clap", VolumeForOneShot(intensity, maxVolume: 0.95));
    }

    public void PlayQuakeHit(double intensity)
    {
        PlayOneShot("quake_hit", VolumeForOneShot(intensity, maxVolume: 0.90));
    }

    private void OnLightningFlash(int portalNumber, double intensity)
    {
        // Throttle a bit so multi-pulse strikes don't create a machine-gun of thunder.
        var now = DateTime.UtcNow;
        if ((now - _lastThunderUtc).TotalMilliseconds < 180)
        {
            return;
        }

        if (!_latestPortals.TryGetValue(portalNumber, out var snap))
        {
            return;
        }

        if (!snap.IsVisible || !snap.Effects.LightningEnabled || !snap.Effects.LightningSoundEnabled)
        {
            return;
        }

        _lastThunderUtc = now;
        PlayLightningThunder(Math.Max(intensity, snap.Effects.LightningIntensity));
    }

    private void OnQuakeStarted(int portalNumber, double intensity)
    {
        if (!_latestPortals.TryGetValue(portalNumber, out var snap))
        {
            return;
        }

        if (!snap.IsVisible || !snap.Effects.QuakeEnabled || !snap.Effects.QuakeSoundEnabled)
        {
            return;
        }

        // Only a single hit sound is desired (no continuous rumble loop).
        PlayQuakeHit(Math.Max(intensity, snap.Effects.QuakeIntensity));
    }

    private static double MaxIntensity(
        IReadOnlyList<PortalEffectsSnapshot> states,
        Func<Models.OverlayEffectsState, bool> enabled,
        Func<Models.OverlayEffectsState, double> intensity)
    {
        var max = 0.0;
        foreach (var p in states)
        {
            if (!p.IsVisible)
            {
                continue;
            }

            var s = p.Effects;
            if (!enabled(s))
            {
                continue;
            }

            var v = intensity(s);
            if (double.IsNaN(v) || double.IsInfinity(v) || v <= 0)
            {
                continue;
            }

            if (v > max)
            {
                max = v;
            }
        }

        return max;
    }

    private static double Clamp01(double v)
        => v <= 0 ? 0 : v >= 1 ? 1 : v;

    private static double NormalizeIntensity0To1(double intensity)
    {
        // Our effects generally use 0.1..5.0, but keep this tolerant.
        return Clamp01(intensity / 5.0);
    }

    private static float VolumeForLoop(double intensity, double maxVolume)
    {
        var t = NormalizeIntensity0To1(intensity);
        var curve = Math.Pow(t, 0.75);
        var v = maxVolume * curve;
        return (float)Clamp01(v);
    }

    private static float VolumeForOneShot(double intensity, double maxVolume)
    {
        var t = NormalizeIntensity0To1(intensity);
        var curve = 0.25 + (0.75 * Math.Pow(t, 0.60));
        var v = maxVolume * curve;
        return (float)Clamp01(v);
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
            try
            {
                handle.Dispose();
            }
            catch
            {
                // ignore
            }
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
                // Important: keep strong references alive until PlaybackStopped.
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
            Trace.WriteLine($"[EffectsAudio] One-shot '{assetBaseName}' failed: {ex.Message}");
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
            Trace.WriteLine($"[EffectsAudio] Loop '{assetBaseName}' failed: {ex.Message}");
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
        {
            _channel.Volume = v;
        }

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
