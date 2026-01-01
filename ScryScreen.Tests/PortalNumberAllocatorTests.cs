using System;
using ScryScreen.Core.Utilities;
using Xunit;

namespace ScryScreen.Tests;

public class PortalNumberAllocatorTests
{
    [Fact]
    public void GetNextAvailable_ThrowsOnNull()
    {
        Assert.Throws<ArgumentNullException>(() => PortalNumberAllocator.GetNextAvailable(null!));
    }

    [Theory]
    [InlineData(new int[0], 1)]
    [InlineData(new[] { 1 }, 2)]
    [InlineData(new[] { 2 }, 1)]
    [InlineData(new[] { 1, 2, 3 }, 4)]
    [InlineData(new[] { 1, 3, 4 }, 2)]
    [InlineData(new[] { 3, 4, 5 }, 1)]
    [InlineData(new[] { 1, 2, 4, 6 }, 3)]
    public void GetNextAvailable_ReturnsLowestUnused(int[] used, int expected)
    {
        Assert.Equal(expected, PortalNumberAllocator.GetNextAvailable(used));
    }
}
