using System;
using ScryScreen.Core.InitiativeTracker;
using Xunit;

namespace ScryScreen.Tests;

public class InitiativeTrackerFormatterTests
{
    [Fact]
    public void EmptyState_ShowsNoCombatants()
    {
        var text = InitiativeTrackerFormatter.ToPortalText(InitiativeTrackerState.Empty);
        Assert.Contains("Round:", text);
        Assert.Contains("No combatants", text);
    }

    [Fact]
    public void MarksActive_WithArrow()
    {
        var a = new InitiativeEntry(Guid.NewGuid(), "A", 10, Mod: 2);
        var b = new InitiativeEntry(Guid.NewGuid(), "B", 5, Mod: 1);
        var state = new InitiativeTrackerState(new[] { a, b }, Round: 2, ActiveId: b.Id);

        var text = InitiativeTrackerFormatter.ToPortalText(state, new InitiativeTrackerFormatter.Options(ShowRound: true, ShowInitiativeValues: true, IncludeHidden: true, MaxEntries: 12));

        Assert.Contains("Round: 2", text);
        Assert.Contains(">", text);
        Assert.Contains("5 (1)", text);
        Assert.Contains("B", text);
    }

    [Fact]
    public void ExcludesHidden_ByDefault()
    {
        var a = new InitiativeEntry(Guid.NewGuid(), "A", 10, Mod: 0, IsHidden: true);
        var b = new InitiativeEntry(Guid.NewGuid(), "B", 5, Mod: 0, IsHidden: false);
        var state = new InitiativeTrackerState(new[] { a, b }, Round: 1, ActiveId: a.Id);

        var text = InitiativeTrackerFormatter.ToPortalText(state);

        Assert.DoesNotContain("A", text);
        Assert.Contains("B", text);
    }

    [Fact]
    public void MaxEntries_ShowsEllipsisAndCount()
    {
        var entries = new InitiativeEntry[5];
        for (var i = 0; i < entries.Length; i++)
        {
            entries[i] = new InitiativeEntry(Guid.NewGuid(), $"E{i}", 10 - i, Mod: i);
        }

        var state = new InitiativeTrackerState(entries, Round: 1, ActiveId: entries[0].Id);
        var text = InitiativeTrackerFormatter.ToPortalText(state, new InitiativeTrackerFormatter.Options(MaxEntries: 2, IncludeHidden: true));

        Assert.Contains("…", text);
        Assert.Contains("3 more", text);
    }
}
