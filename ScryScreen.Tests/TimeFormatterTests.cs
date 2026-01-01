using ScryScreen.Core.Utilities;
using Xunit;

namespace ScryScreen.Tests;

public class TimeFormatterTests
{
    [Theory]
    [InlineData(-1, "0:00")]
    [InlineData(0, "0:00")]
    [InlineData(999, "0:00")]
    [InlineData(1_000, "0:01")]
    [InlineData(59_000, "0:59")]
    [InlineData(60_000, "1:00")]
    [InlineData(61_000, "1:01")]
    [InlineData(3_599_000, "59:59")]
    [InlineData(3_600_000, "1:00:00")]
    [InlineData(3_661_000, "1:01:01")]
    public void FormatMs_ProducesExpectedString(long ms, string expected)
    {
        Assert.Equal(expected, TimeFormatter.FormatMs(ms));
    }
}
