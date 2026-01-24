using System;
using System.Linq;
using System.Text.RegularExpressions;
using ScryScreen.App.Services;
using ScryScreen.App.ViewModels;
using Xunit;

namespace ScryScreen.Tests;

public class DiceRollerD100TensPickTests
{
    private static readonly Regex D100PickRegex = new(
        @"^d100(?<suffix>A{1,3}|D{1,3}) \((?<tens>\d{1,2}(?:,\d{1,2})*) \+ (?<ones>\d)\)(?<mod>(?: \+ \d+| \- \d+)?) = (?<total>\-?\d+)$",
        RegexOptions.Compiled);

    [Theory]
    [InlineData("d100A")]
    [InlineData("d100AA")]
    [InlineData("d100D")]
    [InlineData("d100DD")]
    [InlineData("d100AA+5")]
    [InlineData("d100DD-2")]
    public void Roll_TypedD100Pick_IsSelfConsistent(string expression)
    {
        var vm = new DiceRollerViewModel
        {
            IsTrayAssigned = true,
            Expression = expression,
        };

        vm.RollCommand.Execute(null);

        // Tray-driven: tens are d100 percentile dice; ones is a d10.
        var snapshot = vm.SnapshotState();
        Assert.NotNull(snapshot.RollRequests);
        Assert.NotEmpty(snapshot.RollRequests!);

        var request = Assert.Single(snapshot.RollRequests!);
        Assert.Equal(2, request.Terms.Count);

        var tensTerm = request.Terms.Single(t => t.Sides == 100);
        var onesTerm = request.Terms.Single(t => t.Sides == 10);
        Assert.Equal(1, onesTerm.Count);

        // Simulate tray results.
        // Tens die uses d100, where 100 is treated as 00.
        for (var i = 0; i < tensTerm.Count; i++)
        {
            DiceRollerEventBus.RaiseSingleDieRollCompleted(request.RequestId, 100, 100);
        }

        // Ones die uses d10, where 10 is treated as 0.
        DiceRollerEventBus.RaiseSingleDieRollCompleted(request.RequestId, 10, 10);

        Assert.Null(vm.LastErrorText);

        var match = D100PickRegex.Match(vm.LastResultText);
        Assert.True(match.Success, vm.LastResultText);

        var suffix = match.Groups["suffix"].Value;
        var tens = match.Groups["tens"].Value.Split(',').Select(int.Parse).ToArray();
        var ones = int.Parse(match.Groups["ones"].Value);
        var total = int.Parse(match.Groups["total"].Value);

        var modifierText = match.Groups["mod"].Value;
        var modifier = 0;
        if (!string.IsNullOrWhiteSpace(modifierText))
        {
            modifier = int.Parse(modifierText.Replace(" ", string.Empty));
        }

        var pickedTens = suffix.StartsWith('A') ? tens.Min() : tens.Max();
        var baseValue = pickedTens + ones;
        if (baseValue == 0)
        {
            baseValue = 100;
        }

        var expected = baseValue + modifier;
        Assert.Equal(expected, total);
    }

    [Fact]
    public void Roll_WhenBatchContainsD100AA_FallsBackToRngEvaluation()
    {
        var vm = new DiceRollerViewModel
        {
            IsTrayAssigned = true,
            Expression = "d100AA, 3d6",
        };

        vm.RollCommand.Execute(null);

        Assert.Null(vm.LastErrorText);
        Assert.Contains("d100AA (", vm.LastResultText);
        Assert.Contains("3d6 (", vm.LastResultText);

        var snapshot = vm.SnapshotState();
        Assert.NotNull(snapshot.RollRequests);
        Assert.Empty(snapshot.RollRequests!);
    }
}
