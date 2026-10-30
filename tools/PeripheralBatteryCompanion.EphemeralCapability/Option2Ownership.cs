namespace PeripheralBatteryCompanion.EphemeralCapability;

// Cleanup authority, not native-handle disposition and never proof of policy enforcement.
internal enum O2OwnershipState { NotStarted, CreatePending, CreatedUnbound, ConfiguredUnbound,
    FinalizePending, FinalizedUnbound, BindingPending, BoundUnverified, BoundVerified,
    CreationReleasePending, CreationReleased, ReopenPending, ReopenedUnverified, ReopenedVerified,
    DeletePending, Deleted, Unknown, DeletedEvidenceUncertain }
internal sealed class Option2Ownership
{
    internal O2OwnershipState State { get; private set; }
    internal bool CreationAttempted { get; private set; }
    private void From(O2OwnershipState expected, O2OwnershipState next)
    {
        if (State != expected) throw new InvalidOperationException();
        State = next;
    }
    internal void BeginCreate() { From(O2OwnershipState.NotStarted, O2OwnershipState.CreatePending); CreationAttempted = true; }
    internal void Created(O2HandleState handle)
    {
        if (handle != O2HandleState.Owned) { MarkUncertain(); throw new InvalidOperationException(); }
        From(O2OwnershipState.CreatePending, O2OwnershipState.CreatedUnbound);
    }
    internal void Configured() => From(O2OwnershipState.CreatedUnbound, O2OwnershipState.ConfiguredUnbound);
    internal void BeginFinalize() => From(O2OwnershipState.ConfiguredUnbound, O2OwnershipState.FinalizePending);
    internal void Finalized() => From(O2OwnershipState.FinalizePending, O2OwnershipState.FinalizedUnbound);
    internal void BeginBinding() => From(O2OwnershipState.FinalizedUnbound, O2OwnershipState.BindingPending);
    internal void BindingFlushed() => From(O2OwnershipState.BindingPending, O2OwnershipState.BoundUnverified);
    // Caller must first verify exact live journal AND provider/name/user scope/SPKI independently.
    internal void BindingVerified() => From(O2OwnershipState.BoundUnverified, O2OwnershipState.BoundVerified);
    internal void BeginCreationRelease() => From(O2OwnershipState.BoundVerified, O2OwnershipState.CreationReleasePending);
    internal void CreationReleased(bool confirmed)
    {
        if (!confirmed) { MarkUncertain(); throw new InvalidOperationException(); }
        From(O2OwnershipState.CreationReleasePending, O2OwnershipState.CreationReleased);
    }
    internal void BeginReopen() => From(O2OwnershipState.CreationReleased, O2OwnershipState.ReopenPending);
    internal void Reopened(O2HandleState handle)
    {
        if (handle != O2HandleState.Owned) { MarkUncertain(); throw new InvalidOperationException(); }
        From(O2OwnershipState.ReopenPending, O2OwnershipState.ReopenedUnverified);
    }
    internal void ReopenedVerified() => From(O2OwnershipState.ReopenedUnverified, O2OwnershipState.ReopenedVerified);
    internal bool CleanupEligible(O2HandleState handle) => handle == O2HandleState.Owned &&
        State is O2OwnershipState.BoundVerified or O2OwnershipState.ReopenedVerified;
    internal void BeginDelete(O2HandleState handle)
    {
        if (!CleanupEligible(handle)) throw new InvalidOperationException();
        State = O2OwnershipState.DeletePending;
    }
    internal void Deleted(O2Deletion result, O2HandleState actualHandle)
    {
        if (State != O2OwnershipState.DeletePending || result.Code != 0 || result.State != O2HandleState.ConsumedByDelete || actualHandle != O2HandleState.ConsumedByDelete) {
            MarkUncertain(); throw new InvalidOperationException();
        }
        State = O2OwnershipState.Deleted;
    }
    internal void MarkUncertain()
    {
        if (State is O2OwnershipState.Deleted or O2OwnershipState.DeletedEvidenceUncertain) State = O2OwnershipState.DeletedEvidenceUncertain;
        else if (State != O2OwnershipState.NotStarted) State = O2OwnershipState.Unknown;
    }
    internal void OperationFailed()
    {
        // Known bound identity can support later fresh cleanup checks after a policy/sign/export error.
        // Pending/unbound/unverified operations cannot be converted to cleanup authority on an error.
        if (State is not (O2OwnershipState.NotStarted or O2OwnershipState.BoundVerified or O2OwnershipState.ReopenedVerified)) MarkUncertain();
    }
}
