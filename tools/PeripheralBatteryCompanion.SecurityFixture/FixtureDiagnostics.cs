namespace PeripheralBatteryCompanion.SecurityFixture;

internal enum ExportObservation { ExportSucceeded, StateRejected, AccessDenied, UnsupportedInconclusive, UnexpectedFailure }
internal enum FixtureOperation { Preflight, CreateKey, ReopenKey, ExportEccPrivate, ReadKeyDacl, ReadKeyOwner,
    DuplicateKeyCreate, CreateLeaf, OpenCertificateStore, AssociateLeaf, SignChallenge, RecordEvidence,
    CleanupValidateOwnership, CleanupRemoveLeaf, CleanupDeleteKey, CleanupRecordEvidence }
internal enum CleanupOutcome { NoCreationAttempted, CreationOutcomeUncertain, CreatedCleanupUnconfirmed, CreatedThenCleaned }

// Pure interpretation only. Never invokes native APIs or treats an HRESULT as proof of policy causality.
internal static class FixtureDiagnostics
{
    internal static ExportObservation ClassifyExport(bool returnedBytes, int? hresult) => returnedBytes
        ? ExportObservation.ExportSucceeded : hresult switch {
            unchecked((int)0x8009000B) => ExportObservation.StateRejected,
            unchecked((int)0x80090010) => ExportObservation.AccessDenied,
            unchecked((int)0x80090029) => ExportObservation.UnsupportedInconclusive,
            _ => ExportObservation.UnexpectedFailure
        };

    // These mean a rejection was observed, NOT that export policy alone caused it.
    internal static bool IsRejectionObserved(ExportObservation result) =>
        result is ExportObservation.StateRejected or ExportObservation.AccessDenied;

    internal static string Failure(FixtureOperation operation, Exception error) =>
        $"operation={operation}; category={Category(error)}; HRESULT=0x{error.HResult:X8}";

    private static string Category(Exception error) => error switch {
        System.Security.Cryptography.CryptographicException => "CryptographicException",
        UnauthorizedAccessException => "UnauthorizedAccessException",
        System.ComponentModel.Win32Exception => "Win32Exception",
        IOException => "IOException",
        InvalidOperationException => "InvalidOperationException",
        _ => "OtherException"
    };

    internal static string Cleanup(CleanupOutcome outcome, bool certificatePlanned) => outcome switch {
        CleanupOutcome.NoCreationAttempted => "no-key-creation-attempted; fixture-files-may-remain",
        CleanupOutcome.CreationOutcomeUncertain => "NOT CONFIRMED: key-creation-outcome-uncertain",
        CleanupOutcome.CreatedCleanupUnconfirmed => "NOT CONFIRMED: created-key-cleanup-incomplete",
        CleanupOutcome.CreatedThenCleaned => certificatePlanned
            ? "created-key-and-planned-leaf-cleaned-by-harness; journal-retained; not-independently-verified"
            : "created-key-cleaned-by-harness; leaf-not-created; journal-retained; not-independently-verified",
        _ => "NOT CONFIRMED: unknown-cleanup-state"
    };
}
