using ScryScreen.App.ViewModels;
using Xunit;

namespace ScryScreen.Tests;

public class DiceRollerAdvantageDisadvantageTests
{
    [Fact]
    public void RollD20Advantage_WhenTrayNotAssigned_WritesHistory_AndDoesNotEmitTrayRequests()
    {
        var vm = new DiceRollerViewModel
        {
            IsTrayAssigned = false,
        };

        vm.RollD20AdvantageCommand.Execute(null);

        Assert.Contains("d20A (", vm.LastResultText);
        Assert.DoesNotContain("Rolling ", vm.LastResultText);
        Assert.True(vm.History.Count >= 1);
        var snapshot = vm.SnapshotState();
        Assert.NotNull(snapshot.RollRequests);
        Assert.Empty(snapshot.RollRequests!);
    }

    [Fact]
    public void RollD20Disadvantage_WhenTrayNotAssigned_WritesHistory_AndDoesNotEmitTrayRequests()
    {
        var vm = new DiceRollerViewModel
        {
            IsTrayAssigned = false,
        };

        vm.RollD20DisadvantageCommand.Execute(null);

        Assert.Contains("d20D (", vm.LastResultText);
        Assert.DoesNotContain("Rolling ", vm.LastResultText);
        Assert.True(vm.History.Count >= 1);
        var snapshot = vm.SnapshotState();
        Assert.NotNull(snapshot.RollRequests);
        Assert.Empty(snapshot.RollRequests!);
    }
}
