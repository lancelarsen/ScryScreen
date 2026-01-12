using ScryScreen.Core.Utilities;
using Xunit;

namespace ScryScreen.Tests;

public class MediaFileClassifierTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("C:/x.mp3", true)]
    [InlineData("C:/x.MP3", true)]
    [InlineData("C:/x.wav", true)]
    [InlineData("C:/x.WAV", true)]
    [InlineData("C:/x.mp3   ", true)]
    [InlineData("C:/x.mp3.bak", false)]
    [InlineData("C:/x.mp4", false)]
    [InlineData("C:/x.png", false)]
    public void IsAudio_DetectsSupportedAudio(string? path, bool expected)
    {
        Assert.Equal(expected, MediaFileClassifier.IsAudio(path));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("C:/x.mp4", true)]
    [InlineData("C:/x.MP4", true)]
    [InlineData("C:/x.mp4   ", true)]
    [InlineData("C:/x.mp4.bak", false)]
    [InlineData("C:/x", false)]
    [InlineData("C:/x.", false)]
    [InlineData("C:/x.mkv", false)]
    [InlineData("C:/x.png", false)]
    public void IsVideo_DetectsMp4(string? path, bool expected)
    {
        Assert.Equal(expected, MediaFileClassifier.IsVideo(path));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("C:/x.png", true)]
    [InlineData("C:/x.PNG", true)]
    [InlineData("C:/x.jpg", true)]
    [InlineData("C:/x.jpeg", true)]
    [InlineData("C:/x.JPEG", true)]
    [InlineData("C:/x.bmp", true)]
    [InlineData("C:/x.gif", true)]
    [InlineData("C:/x.jpeg   ", true)]
    [InlineData("C:/x.png.bak", false)]
    [InlineData("C:/x.webp", false)]
    [InlineData("C:/x.mp4", false)]
    [InlineData("C:/x.txt", false)]
    public void IsImage_DetectsSupportedImages(string? path, bool expected)
    {
        Assert.Equal(expected, MediaFileClassifier.IsImage(path));
    }
}
