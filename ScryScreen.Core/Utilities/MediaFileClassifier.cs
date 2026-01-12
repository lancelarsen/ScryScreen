using System;
using System.IO;

namespace ScryScreen.Core.Utilities;

public static class MediaFileClassifier
{
    public static bool IsVideo(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        var ext = Path.GetExtension(filePath.Trim());
        return ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsAudio(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        var ext = Path.GetExtension(filePath.Trim());
        return ext.Equals(".mp3", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".wav", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsImage(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        var ext = Path.GetExtension(filePath.Trim());
        return ext.Equals(".png", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".gif", StringComparison.OrdinalIgnoreCase);
    }
}
