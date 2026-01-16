using System;
using System.Linq;
using ScryScreen.App.Services;
using ScryScreen.App.ViewModels;
using ScryScreen.Core.InitiativeTracker;
using Xunit;

namespace ScryScreen.Tests;

public sealed class InitiativePortalStylingTests
{
    [Fact]
    public void UnknownMax_UsesDefaultWhite()
    {
        var entry = new InitiativeEntry(
            Id: Guid.NewGuid(),
            Name: "A",
            Initiative: 10,
            MaxHp: "",
            CurrentHp: "5");

        var vm = new InitiativePortalViewModel(new InitiativeTrackerState(new[] { entry }, Round: 1, ActiveId: entry.Id));
        Assert.False(vm.Entries[0].HasHealthDot);
        Assert.False(vm.Entries[0].IsStrikethrough);
    }

    [Fact]
    public void MaxOnly_UsesGreen()
    {
        var entry = new InitiativeEntry(
            Id: Guid.NewGuid(),
            Name: "A",
            Initiative: 10,
            MaxHp: "10",
            CurrentHp: "");

        var vm = new InitiativePortalViewModel(new InitiativeTrackerState(new[] { entry }, Round: 1, ActiveId: entry.Id));
        Assert.True(vm.Entries[0].HasHealthDot);
        Assert.Equal("#FF22C55E", vm.Entries[0].HealthDotColorHex);
    }

    [Fact]
    public void AtOrBelowHalf_UsesRed_AndZeroStrikesThrough()
    {
        var half = new InitiativeEntry(
            Id: Guid.NewGuid(),
            Name: "A",
            Initiative: 10,
            MaxHp: "10",
            CurrentHp: "5");

        var zero = new InitiativeEntry(
            Id: Guid.NewGuid(),
            Name: "B",
            Initiative: 9,
            MaxHp: "10",
            CurrentHp: "0");

        var vm = new InitiativePortalViewModel(new InitiativeTrackerState(new[] { half, zero }, Round: 1, ActiveId: half.Id));
        Assert.Equal("#FFDC2626", vm.Entries.Single(e => e.Name == "A").HealthDotColorHex);
        Assert.Equal("#FFDC2626", vm.Entries.Single(e => e.Name == "B").HealthDotColorHex);
        Assert.True(vm.Entries.Single(e => e.Name == "B").IsStrikethrough);
    }

    [Fact]
    public void BloodiedCondition_ForcesRed_WithoutHp()
    {
        var entry = new InitiativeEntry(
            Id: Guid.NewGuid(),
            Name: "A",
            Initiative: 10,
            MaxHp: "",
            CurrentHp: "",
            Conditions: new[] { new AppliedCondition(ConditionLibraryService.BloodiedId, null) });

        var vm = new InitiativePortalViewModel(new InitiativeTrackerState(new[] { entry }, Round: 1, ActiveId: entry.Id));
        Assert.Equal("#FFDC2626", vm.Entries[0].HealthDotColorHex);
    }

    [Fact]
    public void DeadCondition_ForcesStrikethrough_WithoutHp()
    {
        var entry = new InitiativeEntry(
            Id: Guid.NewGuid(),
            Name: "A",
            Initiative: 10,
            MaxHp: "10",
            CurrentHp: "10",
            Conditions: new[] { new AppliedCondition(ConditionLibraryService.DeadId, null) });

        var vm = new InitiativePortalViewModel(new InitiativeTrackerState(new[] { entry }, Round: 1, ActiveId: entry.Id));
        Assert.True(vm.Entries[0].IsStrikethrough);
        Assert.Equal("#FF9CA3AF", vm.Entries[0].HealthDotColorHex);
    }
}
