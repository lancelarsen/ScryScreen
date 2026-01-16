using System;
using ScryScreen.Core.Utilities;
using Xunit;

namespace ScryScreen.Tests;

public class DiceExpressionEvaluatorTests
{
    [Theory]
    [InlineData("d20", 1, 20)]
    [InlineData("1d6", 1, 6)]
    [InlineData("2d6+3", 5, 15)]
    [InlineData("1d4-2", -1, 2)]
    public void TryEvaluate_ReturnsTotalWithinExpectedRange(string expression, int min, int max)
    {
        var rng = new Random(1234);
        var ok = DiceExpressionEvaluator.TryEvaluate(expression, rng, out var result, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.InRange(result.Total, min, max);
        Assert.False(string.IsNullOrWhiteSpace(result.DisplayText));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("2dd6")]
    [InlineData("d")]
    [InlineData("+")]
    [InlineData("1d6+")]
    public void TryEvaluate_InvalidExpressionsFail(string expression)
    {
        var rng = new Random(1234);
        var ok = DiceExpressionEvaluator.TryEvaluate(expression, rng, out _, out var error);

        Assert.False(ok);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}
