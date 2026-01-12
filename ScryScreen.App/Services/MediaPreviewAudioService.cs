using System;
using System.Diagnostics;
using NAudio.Wave;

namespace ScryScreen.App.Services;

internal sealed class MediaPreviewAudioService : IDisposable
{
    private bool _isStopping;
    private WaveOutEvent? _output;
    private WaveStream? _playback;
    private WaveChannel32? _channel;
    private AudioFileReader? _reader;

    public string? CurrentFilePath { get; private set; }

    public bool IsPlaying { get; private set; }

    public TimeSpan CurrentTime
    {
        get
        {
            try { return _reader?.CurrentTime ?? TimeSpan.Zero; }
            catch { return TimeSpan.Zero; }
        }
    }

    public TimeSpan TotalTime
    {
        get
        {
            try { return _reader?.TotalTime ?? TimeSpan.Zero; }
            catch { return TimeSpan.Zero; }
        }
    }

    public event EventHandler<string>? PlaybackFinished;

    public void Play(string filePath, bool loop)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        Stop();

        try
        {
            CurrentFilePath = filePath;

            _reader = new AudioFileReader(filePath);
            _channel = new WaveChannel32(_reader) { PadWithZeroes = false, Volume = 1.0f };
            _playback = loop ? new LoopStream(_channel) : _channel;

            _output = new WaveOutEvent();
            _output.Init(_playback);
            _output.PlaybackStopped += (_, _) =>
            {
                var finishedFile = CurrentFilePath;
                Stop(stopOutputDevice: false);

                if (!string.IsNullOrWhiteSpace(finishedFile))
                {
                    PlaybackFinished?.Invoke(this, finishedFile);
                }
            };

            IsPlaying = true;
            _output.Play();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[MediaPreviewAudio] Play failed: {ex.Message}");
            Stop();
        }
    }

    public void Stop()
        => Stop(stopOutputDevice: true);

    private void Stop(bool stopOutputDevice)
    {
        if (_isStopping)
        {
            return;
        }

        _isStopping = true;

        IsPlaying = false;
        CurrentFilePath = null;

        if (stopOutputDevice)
        {
            try { _output?.Stop(); } catch { }
        }

        try { _output?.Dispose(); } catch { }
        try { _playback?.Dispose(); } catch { }
        try { _channel?.Dispose(); } catch { }
        try { _reader?.Dispose(); } catch { }

        _output = null;
        _playback = null;
        _channel = null;
        _reader = null;

        _isStopping = false;
    }

    public void Dispose()
    {
        Stop();
    }
}
