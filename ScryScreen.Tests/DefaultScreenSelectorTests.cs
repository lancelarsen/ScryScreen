using ScryScreen.Core.Utilities;
using Xunit;

namespace ScryScreen.Tests;

public class DefaultScreenSelectorTests
{
    [Fact]
    public void Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => DefaultScreenSelector.GetDefaultScreenIndex(null!, isFirstPortal: true));
    }

    [Fact]
    public void Empty_ReturnsMinusOne()
    {
        Assert.Equal(-1, DefaultScreenSelector.GetDefaultScreenIndex(Array.Empty<bool>(), isFirstPortal: true));
        Assert.Equal(-1, DefaultScreenSelector.GetDefaultScreenIndex(Array.Empty<bool>(), isFirstPortal: false));
    }

    [Fact]
    public void FirstPortal_PrefersFirstNonPrimary()
    {
        // [Primary, NonPrimary, NonPrimary]
        Assert.Equal(1, DefaultScreenSelector.GetDefaultScreenIndex(new[] { true, false, false }, isFirstPortal: true));

        // [Primary, Primary]
        Assert.Equal(0, DefaultScreenSelector.GetDefaultScreenIndex(new[] { true, true }, isFirstPortal: true));

        // [NonPrimary, Primary]
        Assert.Equal(0, DefaultScreenSelector.GetDefaultScreenIndex(new[] { false, true }, isFirstPortal: true));
    }

    [Fact]
    public void NotFirstPortal_AlwaysUsesFirstScreen()
    {
        Assert.Equal(0, DefaultScreenSelector.GetDefaultScreenIndex(new[] { true, false, false }, isFirstPortal: false));
        Assert.Equal(0, DefaultScreenSelector.GetDefaultScreenIndex(new[] { false, true }, isFirstPortal: false));
    }
}
