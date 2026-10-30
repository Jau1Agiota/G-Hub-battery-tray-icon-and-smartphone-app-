using PeripheralBatteryCompanion.EphemeralCapability;
using Xunit;

namespace PeripheralBatteryCompanion.Tests;

public sealed class Option2OwnershipTests
{
    private static List<Option2Ownership> States()
    {
        var result = new List<Option2Ownership>();
        for (int stop = 0; stop <= 15; stop++) {
            var state = new Option2Ownership();
            Action[] steps = [state.BeginCreate, () => state.Created(O2HandleState.Owned), state.Configured,
                state.BeginFinalize, state.Finalized, state.BeginBinding, state.BindingFlushed,
                state.BindingVerified, state.BeginCreationRelease, () => state.CreationReleased(true),
                state.BeginReopen, () => state.Reopened(O2HandleState.Owned), state.ReopenedVerified,
                () => state.BeginDelete(O2HandleState.Owned),
                () => state.Deleted(new(0, O2HandleState.ConsumedByDelete), O2HandleState.ConsumedByDelete)];
            foreach (var step in steps.Take(stop)) step();
            result.Add(state);
        }
        return result;
    }

    [Fact]
    public void EveryLifecycleBoundaryRequiresVerifiedIdentityAndOwnedHandle()
    {
        foreach (var state in States()) {
            foreach (var handle in Enum.GetValues<O2HandleState>())
                Assert.Equal(handle == O2HandleState.Owned &&
                    state.State is O2OwnershipState.BoundVerified or O2OwnershipState.ReopenedVerified,
                    state.CleanupEligible(handle));
            if (!state.CleanupEligible(O2HandleState.Owned))
                Assert.Throws<InvalidOperationException>(() => state.BeginDelete(O2HandleState.Owned));
        }
    }

    [Fact]
    public void FailureAtEveryBoundaryCannotPromoteProvisionalOrPendingProof()
    {
        foreach (var state in States()) {
            var before = state.State; state.OperationFailed();
            Assert.Equal(before is O2OwnershipState.BoundVerified or O2OwnershipState.ReopenedVerified,
                state.CleanupEligible(O2HandleState.Owned));
            state.MarkUncertain();
            Assert.False(state.CleanupEligible(O2HandleState.Owned));
            Assert.Throws<InvalidOperationException>(state.BindingVerified);
            Assert.Throws<InvalidOperationException>(state.ReopenedVerified);
        }
    }

    [Fact]
    public void ConfirmedDeleteFollowedByEvidenceFailureNeverPermitsAnotherDelete()
    {
        var state = States().Last();
        state.MarkUncertain();
        Assert.Equal(O2OwnershipState.DeletedEvidenceUncertain, state.State);
        Assert.False(state.CleanupEligible(O2HandleState.Owned));
        Assert.Throws<InvalidOperationException>(() => state.BeginDelete(O2HandleState.Owned));
    }

    [Theory]
    [InlineData("ff28360e68f249439294475e8da21614")]
    [InlineData("FF28360E68F249439294475E8DA21614")]
    public void PreviewOnlyIdIsNotAnExecutionPermit(string id)
    {
        Assert.False(O2Permit.ValidId(id));
        Assert.Null(O2Permit.Create(O2Result.Contract, id, O2Permit.Confirmation, O2Permit.UiAcknowledgement));
    }
}
