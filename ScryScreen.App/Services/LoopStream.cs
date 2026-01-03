using System;
using NAudio.Wave;

namespace ScryScreen.App.Services;

internal sealed class LoopStream : WaveStream
{
    private readonly WaveStream _source;

    public LoopStream(WaveStream source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public override WaveFormat WaveFormat => _source.WaveFormat;

    public override long Length => long.MaxValue;

    public override long Position
    {
        get => _source.Position;
        set => _source.Position = value;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var totalRead = 0;

        while (totalRead < count)
        {
            var read = _source.Read(buffer, offset + totalRead, count - totalRead);
            if (read == 0)
            {
                _source.Position = 0;
                continue;
            }

            totalRead += read;
        }

        return totalRead;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _source.Dispose();
        }

        base.Dispose(disposing);
    }
}
