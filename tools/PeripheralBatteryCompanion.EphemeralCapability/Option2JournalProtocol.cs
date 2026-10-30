namespace PeripheralBatteryCompanion.EphemeralCapability;

internal static class Option2JournalProtocol
{
    internal static bool Allowed(O2Stage? current, O2Stage next, ReadOnlySpan<byte> binding) => next switch {
        O2Stage.Planned => current is null,
        O2Stage.Created => current == O2Stage.Planned,
        O2Stage.Finalized => current == O2Stage.Created,
        O2Stage.Bound => current == O2Stage.Finalized && O2Binding.Valid(binding),
        O2Stage.Reopened => current == O2Stage.Bound,
        O2Stage.Validated => current == O2Stage.Reopened,
        O2Stage.PossessionVerified => current == O2Stage.Validated,
        O2Stage.ExportAttempted => current == O2Stage.PossessionVerified,
        O2Stage.ExportRejected => current == O2Stage.ExportAttempted,
        O2Stage.UnexpectedOutput => current is O2Stage.ExportAttempted or O2Stage.ExportRejected,
        O2Stage.DeleteAttempted => current is O2Stage.Bound or O2Stage.Reopened or O2Stage.Validated or
            O2Stage.PossessionVerified or O2Stage.ExportAttempted or O2Stage.ExportRejected or O2Stage.UnexpectedOutput,
        O2Stage.Deleted => current == O2Stage.DeleteAttempted,
        O2Stage.Completed => current == O2Stage.Deleted,
        O2Stage.CleanupUnconfirmed => current is not null and not O2Stage.Completed and not O2Stage.CleanupUnconfirmed,
        _ => false
    };
}
