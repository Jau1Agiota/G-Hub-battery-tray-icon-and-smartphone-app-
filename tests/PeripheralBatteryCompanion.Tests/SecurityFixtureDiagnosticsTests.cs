using PeripheralBatteryCompanion.SecurityFixture;
using Xunit;

namespace PeripheralBatteryCompanion.Tests;

// PURE tests only. No cryptographic operations, key/certificate creation, store access or filesystem writes.
public sealed class SecurityFixtureDiagnosticsTests
{
    [Theory]
    [InlineData(false, unchecked((int)0x80090029), (int)ExportObservation.UnsupportedInconclusive, false)]
    [InlineData(false, unchecked((int)0x8009000B), (int)ExportObservation.StateRejected, true)]
    [InlineData(false, unchecked((int)0x80090010), (int)ExportObservation.AccessDenied, true)]
    [InlineData(false, unchecked((int)0x80090011), (int)ExportObservation.UnexpectedFailure, false)]
    [InlineData(false, null, (int)ExportObservation.UnexpectedFailure, false)]
    [InlineData(true, null, (int)ExportObservation.ExportSucceeded, false)]
    [InlineData(true, unchecked((int)0x80090010), (int)ExportObservation.ExportSucceeded, false)]
    public void ExportResultPreservesUnsupportedAndUnexpectedFailures(bool returned, int? code,
        int expected, bool rejectionObserved)
    {
        var result = FixtureDiagnostics.ClassifyExport(returned, code);
        Assert.Equal((ExportObservation)expected, result);
        Assert.Equal(rejectionObserved, FixtureDiagnostics.IsRejectionObserved(result));
    }

    [Fact]
    public void FailureContainsOnlyFixedOperationSafeCategoryAndCode()
    {
        var error = new System.Security.Cryptography.CryptographicException(unchecked((int)0x80090029));
        Assert.Equal("operation=ExportEccPrivate; category=CryptographicException; HRESULT=0x80090029",
            FixtureDiagnostics.Failure(FixtureOperation.ExportEccPrivate, error));
        var privateMessage = new IOException("PRIVATE user-path SID key-name private-bytes");
        Assert.DoesNotContain("PRIVATE", FixtureDiagnostics.Failure(FixtureOperation.CleanupDeleteKey, privateMessage));
        Assert.Equal("operation=CleanupDeleteKey; category=IOException; HRESULT=0x80131620",
            FixtureDiagnostics.Failure(FixtureOperation.CleanupDeleteKey, privateMessage));
    }

    [Fact]
    public void CreatedThenCleanedNeverReportsNoKeyCreatedAndDoesNotClaimIndependentProof()
    {
        var keyOnly = FixtureDiagnostics.Cleanup(CleanupOutcome.CreatedThenCleaned, false);
        Assert.Contains("created-key-cleaned-by-harness", keyOnly);
        Assert.Contains("leaf-not-created", keyOnly);
        Assert.Contains("not-independently-verified", keyOnly);
        Assert.DoesNotContain("no-key", keyOnly);
        Assert.Contains("created-key-and-planned-leaf-cleaned-by-harness",
            FixtureDiagnostics.Cleanup(CleanupOutcome.CreatedThenCleaned, true));
    }

    [Fact]
    public void UncertainAndUnconfirmedCleanupNeverReportsSuccess()
    {
        foreach (var status in new[] { CleanupOutcome.CreationOutcomeUncertain, CleanupOutcome.CreatedCleanupUnconfirmed, (CleanupOutcome)999 })
            Assert.StartsWith("NOT CONFIRMED:", FixtureDiagnostics.Cleanup(status, false));
        Assert.Equal("no-key-creation-attempted; fixture-files-may-remain",
            FixtureDiagnostics.Cleanup(CleanupOutcome.NoCreationAttempted, false));
    }

    [Fact]
    public void FailedOperationIsRetiredEvenIfItsResourcesWereRemoved()
    {
        const string retired = "3c4a7c190ef34f2e90625276aa9cbfdb";
        Assert.False(FixtureConsent.ValidArguments(["--confirm-fixture-creation", retired]));
        Assert.False(FixtureConsent.ValidArguments(["--confirm-fixture-creation", retired.ToUpperInvariant()]));
        Assert.False(FixtureConsent.CanDeleteKey(retired, "PBC-A22C5-Fixture-" + retired, [1], [1], true));
    }
}
