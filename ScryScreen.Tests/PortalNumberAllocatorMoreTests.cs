using System;
using ScryScreen.Core.Utilities;
using Xunit;

namespace ScryScreen.Tests;

public class PortalNumberAllocatorMoreTests
{
    [Fact]
    public void Empty_ReturnsOne()
    {
        Assert.Equal(1, PortalNumberAllocator.GetNextAvailable(Array.Empty<int>()));
    }

    [Fact]
    public void SkipsUsedNumbersAndReturnsLowestGap()
    {
        Assert.Equal(3, PortalNumberAllocator.GetNextAvailable(new[] { 1, 2, 4 }));
    }

    [Fact]
    public void IgnoresOrderAndDuplicates()
    {
        Assert.Equal(4, PortalNumberAllocator.GetNextAvailable(new[] { 2, 3, 1, 3, 2, 1 }));
    }

    [Fact]
    public void NonPositiveValuesDoNotAffectStartAtOne()
    {
        Assert.Equal(1, PortalNumberAllocator.GetNextAvailable(new[] { 0, -1, -5 }));
        Assert.Equal(2, PortalNumberAllocator.GetNextAvailable(new[] { 1, 0, -1 }));
    }
}
