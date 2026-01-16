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
        Assert.Null(vm.Entries[0].HealthIconValue);
        Assert.Equal("/Assets/Icons/sword_rose.svg", vm.Entries[0].HealthSvgPath);
        Assert.Equal("#FF22C55E", vm.Entries[0].HealthDotColorHex);
    }

    [Fact]
    public void AtOrBelowHalf_UsesBloodiedRed_AndZeroUsesDeadSkull()
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
        Assert.Null(vm.Entries.Single(e => e.Name == "A").HealthIconValue);
        Assert.Equal("/Assets/Icons/water_drop.svg", vm.Entries.Single(e => e.Name == "A").HealthSvgPath);
        Assert.Equal("#FFDC2626", vm.Entries.Single(e => e.Name == "A").HealthDotColorHex);

        Assert.Null(vm.Entries.Single(e => e.Name == "B").HealthIconValue);
        Assert.Equal("/Assets/Icons/skull.svg", vm.Entries.Single(e => e.Name == "B").HealthSvgPath);
        Assert.Equal("#FFFFFFFF", vm.Entries.Single(e => e.Name == "B").HealthDotColorHex);
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
        Assert.Null(vm.Entries[0].HealthIconValue);
        Assert.Equal("/Assets/Icons/water_drop.svg", vm.Entries[0].HealthSvgPath);
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
        Assert.Null(vm.Entries[0].HealthIconValue);
        Assert.Equal("/Assets/Icons/skull.svg", vm.Entries[0].HealthSvgPath);
        Assert.Equal("#FFFFFFFF", vm.Entries[0].HealthDotColorHex);
    }

    [Fact]
    public void AboveHalfButNotFull_UsesAmber()
    {
        var entry = new InitiativeEntry(
            Id: Guid.NewGuid(),
            Name: "A",
            Initiative: 10,
            MaxHp: "10",
            CurrentHp: "7");

        var vm = new InitiativePortalViewModel(new InitiativeTrackerState(new[] { entry }, Round: 1, ActiveId: entry.Id));
        Assert.Null(vm.Entries[0].HealthIconValue);
        Assert.Equal("/Assets/Icons/healing.svg", vm.Entries[0].HealthSvgPath);
        Assert.Equal("#FFF59E0B", vm.Entries[0].HealthDotColorHex);
    }
}
