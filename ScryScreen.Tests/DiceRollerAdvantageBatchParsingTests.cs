using ScryScreen.App.ViewModels;
using Xunit;

namespace ScryScreen.Tests;

public class DiceRollerAdvantageBatchParsingTests
{
    [Fact]
    public void Roll_WhenBatchContainsD20A_DoesNotTreatCommaAsModifier()
    {
        var vm = new DiceRollerViewModel
        {
            IsTrayAssigned = true,
            Expression = "d20A, 3d6",
        };

        vm.RollCommand.Execute(null);

        Assert.Null(vm.LastErrorText);
        Assert.Contains("d20A (", vm.LastResultText);
        Assert.Contains("3d6 (", vm.LastResultText);

        var snapshot = vm.SnapshotState();
        Assert.NotNull(snapshot.RollRequests);
        Assert.Empty(snapshot.RollRequests!);
    }
}
