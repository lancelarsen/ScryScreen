using System;
using System.Linq;
using ScryScreen.Core.InitiativeTracker;
using Xunit;

namespace ScryScreen.Tests;

public class InitiativeTrackerEngineTests
{
    [Fact]
    public void Add_NormalizesEmptyName_AndSetsActiveIfMissing()
    {
        var state = InitiativeTrackerState.Empty;
        var entry = new InitiativeEntry(Guid.NewGuid(), "   ", 12, Mod: 3);

        state = InitiativeTrackerEngine.Add(state, entry);

        Assert.Single(state.Entries);
        Assert.Equal("(Unnamed)", state.Entries[0].Name);
        Assert.Equal(entry.Id, state.ActiveId);
        Assert.Equal(1, state.Round);
    }

    [Fact]
    public void NormalizeState_ClampsRound_AndActiveToFirst()
    {
        var a = new InitiativeEntry(Guid.NewGuid(), "A", 10, Mod: 2);
        var b = new InitiativeEntry(Guid.NewGuid(), "B", 5, Mod: 1);

        var state = new InitiativeTrackerState(new[] { a, b }, Round: 0, ActiveId: Guid.NewGuid());
        state = InitiativeTrackerEngine.NormalizeState(state);

        Assert.Equal(1, state.Round);
        Assert.Equal(a.Id, state.ActiveId);
    }

    [Fact]
    public void Sort_SortsByInitiativeDesc_ThenModDesc_ThenName_ThenId()
    {
        var id1 = new Guid("00000000-0000-0000-0000-000000000001");
        var id2 = new Guid("00000000-0000-0000-0000-000000000002");
        var id3 = new Guid("00000000-0000-0000-0000-000000000003");
        var id4 = new Guid("00000000-0000-0000-0000-000000000004");

        var a = new InitiativeEntry(id1, "zeta", 10, Mod: 0);
        var b = new InitiativeEntry(id2, "Alpha", 10, Mod: 5);
        var c = new InitiativeEntry(id3, "Alpha", 10, Mod: 5);
        var d = new InitiativeEntry(id4, "Aardvark", 10, Mod: 0);

        var state = new InitiativeTrackerState(new[] { a, b, c, d }, Round: 1, ActiveId: a.Id);
        state = InitiativeTrackerEngine.Sort(state);

        Assert.Equal(new[] { b.Id, c.Id, d.Id, a.Id }, state.Entries.Select(e => e.Id).ToArray());
        Assert.Equal(a.Id, state.ActiveId); // active preserved when present
    }

    [Fact]
    public void NextTurn_WrapsAndIncrementsRound()
    {
        var a = new InitiativeEntry(Guid.NewGuid(), "A", 10, Mod: 2);
        var b = new InitiativeEntry(Guid.NewGuid(), "B", 5, Mod: 1);

        var state = new InitiativeTrackerState(new[] { a, b }, Round: 1, ActiveId: a.Id);

        state = InitiativeTrackerEngine.NextTurn(state);
        Assert.Equal(b.Id, state.ActiveId);
        Assert.Equal(1, state.Round);

        state = InitiativeTrackerEngine.NextTurn(state);
        Assert.Equal(a.Id, state.ActiveId);
        Assert.Equal(2, state.Round);
    }

    [Fact]
    public void PreviousTurn_WrapsAndDecrementsRoundButNotBelowOne()
    {
        var a = new InitiativeEntry(Guid.NewGuid(), "A", 10, Mod: 2);
        var b = new InitiativeEntry(Guid.NewGuid(), "B", 5, Mod: 1);

        var state = new InitiativeTrackerState(new[] { a, b }, Round: 1, ActiveId: a.Id);
        state = InitiativeTrackerEngine.PreviousTurn(state);

        Assert.Equal(b.Id, state.ActiveId);
        Assert.Equal(1, state.Round);

        // Wrap case: active at the first entry.
        state = state with { ActiveId = a.Id };
        state = InitiativeTrackerEngine.SetRound(state, 3);
        state = InitiativeTrackerEngine.PreviousTurn(state);

        Assert.Equal(b.Id, state.ActiveId);
        Assert.Equal(2, state.Round);
    }

    [Fact]
    public void Remove_RemovesEntry_AndIfActiveRemoved_SelectsNext()
    {
        var a = new InitiativeEntry(Guid.NewGuid(), "A", 10, Mod: 2);
        var b = new InitiativeEntry(Guid.NewGuid(), "B", 5, Mod: 1);
        var c = new InitiativeEntry(Guid.NewGuid(), "C", 1, Mod: 0);

        var state = new InitiativeTrackerState(new[] { a, b, c }, Round: 1, ActiveId: b.Id);
        state = InitiativeTrackerEngine.Remove(state, b.Id);

        Assert.Equal(2, state.Entries.Length);
        Assert.DoesNotContain(state.Entries, e => e.Id == b.Id);
        Assert.Equal(c.Id, state.ActiveId);
    }

    [Fact]
    public void Remove_LastEntry_ClearsActive()
    {
        var a = new InitiativeEntry(Guid.NewGuid(), "A", 10, Mod: 0);
        var state = new InitiativeTrackerState(new[] { a }, Round: 1, ActiveId: a.Id);

        state = InitiativeTrackerEngine.Remove(state, a.Id);

        Assert.Empty(state.Entries);
        Assert.Null(state.ActiveId);
    }
}
