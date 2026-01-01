using System;
using LibVLCSharp.Shared;

namespace ScryScreen.App.Services;

public sealed class LibVlcMediaFactory : IVideoMediaFactory<Media>
{
    private readonly LibVLC _libVlc;

    public LibVlcMediaFactory(LibVLC libVlc)
    {
        _libVlc = libVlc ?? throw new ArgumentNullException(nameof(libVlc));
    }

    public Media CreateFromPath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("File path is required", nameof(filePath));
        }

        return new Media(_libVlc, new Uri(filePath));
    }

    public void Dispose(Media media)
    {
        try
        {
            media.Dispose();
        }
        catch
        {
            // ignore
        }
    }
}
