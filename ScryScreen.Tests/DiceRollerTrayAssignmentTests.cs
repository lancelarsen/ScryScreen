using ScryScreen.App.ViewModels;
using Xunit;

namespace ScryScreen.Tests;

public class DiceRollerTrayAssignmentTests
{
    [Fact]
    public void Roll_WhenTrayNotAssigned_DoesNotEmitTrayRequests_AndWritesHistory()
    {
        var vm = new DiceRollerViewModel
        {
            IsTrayAssigned = false,
            Expression = "d20",
        };

        vm.RollCommand.Execute(null);

        Assert.False(string.IsNullOrWhiteSpace(vm.LastResultText));
        Assert.DoesNotContain("Rolling ", vm.LastResultText);
        Assert.Contains("d20(", vm.LastResultText);
        Assert.True(vm.History.Count >= 1);

        var snapshot = vm.SnapshotState();
        Assert.NotNull(snapshot.RollRequests);
        Assert.Empty(snapshot.RollRequests!);
    }
}
