using System;
using System.Linq;
using ScryScreen.App.ViewModels;
using Xunit;

namespace ScryScreen.Tests;

public sealed class InitiativeConditionTickDownTests
{
    [Fact]
    public void NextTurn_DecrementsTimedConditions_AndExpiresAtZero()
    {
        var vm = new InitiativeTrackerViewModel();

        var entry = vm.Entries[0];
        entry.Name = "Alice";
        entry.Initiative = "10";

        var def = vm.ConditionDefinitions.First(d => !d.IsManualOnly);
        entry.Conditions.Add(new InitiativeEntryConditionViewModel(
            owner: entry,
            conditionId: def.Id,
            name: def.DisplayName,
            colorHex: def.ColorHex,
            isManualOnly: def.IsManualOnly,
            roundsRemaining: 2));

        vm.NextTurnCommand.Execute(null);
        Assert.Single(entry.Conditions);
        Assert.Equal(1, entry.Conditions[0].RoundsRemaining);

        vm.NextTurnCommand.Execute(null);
        Assert.Empty(entry.Conditions);
    }

    [Fact]
    public void NextTurn_DoesNotTickDown_WhenCurrentActiveIsIneligible()
    {
        var vm = new InitiativeTrackerViewModel();

        var ineligible = vm.Entries[0];
        ineligible.Name = "Bob";
        ineligible.Initiative = ""; // ineligible

        var eligible = vm.Entries[1];
        eligible.Name = "Carol";
        eligible.Initiative = "10";

        var def = vm.ConditionDefinitions.First(d => !d.IsManualOnly);
        ineligible.Conditions.Add(new InitiativeEntryConditionViewModel(
            owner: ineligible,
            conditionId: def.Id,
            name: def.DisplayName,
            colorHex: def.ColorHex,
            isManualOnly: def.IsManualOnly,
            roundsRemaining: 2));

        // Active starts on the first row, which is ineligible. Advancing should move active to the eligible row
        // without decrementing the ineligible row's conditions.
        vm.NextTurnCommand.Execute(null);
        Assert.Single(ineligible.Conditions);
        Assert.Equal(2, ineligible.Conditions[0].RoundsRemaining);
    }

    [Fact]
    public void ReAddingCondition_ReplacesExistingInstance()
    {
        var vm = new InitiativeTrackerViewModel();

        var entry = vm.Entries[0];
        entry.Name = "Alice";
        entry.Initiative = "10";

        var def = vm.ConditionDefinitions.First(d => !d.IsManualOnly);
        entry.SelectedConditionToAdd = def;
        entry.SelectedConditionRoundsToAdd = 3;
        vm.AddConditionToEntryCommand.Execute(entry);

        Assert.Single(entry.Conditions);
        Assert.Equal(3, entry.Conditions[0].RoundsRemaining);

        entry.SelectedConditionRoundsToAdd = 1;
        vm.AddConditionToEntryCommand.Execute(entry);
        Assert.Single(entry.Conditions);
        Assert.Equal(1, entry.Conditions[0].RoundsRemaining);
    }

    [Fact]
    public void DeletingCustomCondition_PurgesFromAllEntries()
    {
        var vm = new InitiativeTrackerViewModel();

        // Make the name unique to avoid collisions with any persisted user condition library.
        var name = $"My Custom {Guid.NewGuid():N}";

        var entry = vm.Entries[0];
        entry.Name = "Alice";
        entry.Initiative = "10";

        vm.NewCustomConditionNameText = name;
        vm.NewCustomConditionColorHexText = "#FF3B82F6";
        vm.AddCustomConditionCommand.Execute(null);

        var custom = vm.ConditionDefinitions.Single(d => d.Name == name);
        entry.SelectedConditionToAdd = custom;
        entry.SelectedConditionRoundsToAdd = 2;
        vm.AddConditionToEntryCommand.Execute(entry);

        Assert.Single(entry.Conditions);
        Assert.Equal(custom.Id, entry.Conditions[0].ConditionId);

        vm.SelectedCondition = vm.ConditionLibraryItems.Single(c => c.Id == custom.Id);
        vm.DeleteSelectedCustomConditionCommand.Execute(null);

        Assert.Empty(entry.Conditions);
        Assert.DoesNotContain(vm.ConditionDefinitions, d => d.Id == custom.Id);
    }
}
