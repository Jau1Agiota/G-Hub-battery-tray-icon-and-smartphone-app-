using System.Text;
using PeripheralBatteryCompanion.Core.TransportSecurity;
using Xunit;

namespace PeripheralBatteryCompanion.Tests;

public sealed class EnrollmentAuthorizationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid IdentityId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid EnrollmentId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid PhoneId = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static EnrollmentIdentity Identity => new(IdentityId, "https://192.168.1.10:8443", new string('a', 64),
        [1, 2, 3], Now.AddDays(-1), Now.AddDays(89)); // Synthetic bytes, NOT a validated certificate.
    private static EnrollmentClaim Claim => new(EnrollmentId, PhoneId, new string('A', 43), new string('b', 64));

    [Theory]
    [InlineData("http://192.168.1.10:8443")]
    [InlineData("https://192.168.1.10:8443/")]
    [InlineData("https://192.168.1.10:8443/path")]
    [InlineData("https://192.168.1.10:8443?x=1")]
    [InlineData("https://192.168.1.10:8443#x")]
    [InlineData("https://u@192.168.1.10:8443")]
    [InlineData("https://192.168.001.010:8443")]
    [InlineData("https://192.168.1.10:08443")]
    [InlineData("https://192.168.1.10")]
    [InlineData("https://192.168.1.10:443")]
    [InlineData("https://example.invalid:8443")]
    [InlineData("https://127.0.0.1:8443")]
    [InlineData("https://8.8.8.8:8443")]
    [InlineData("https://[fd00::1]:8443")]
    [InlineData("HTTPS://192.168.1.10:8443")]
    public void EndpointRejectsSubstitutionAndNoncanonicalForms(string endpoint) =>
        Assert.False(EnrollmentIdentity.ValidEndpoint(endpoint));

    [Fact]
    public void TranscriptHasExactContractBytesAndNoPrivateMaterial()
    {
        string expected = "PBC-ENROLL-1\nhttps://192.168.1.10:8443\n" + new string('a', 64) +
            "\n22222222-2222-4222-8222-222222222222\n33333333-3333-4333-8333-333333333333\n" +
            new string('A', 43) + "\n" + new string('b', 64) + "\n";
        Assert.Equal(Encoding.ASCII.GetBytes(expected), Claim.TranscriptBytes(Identity));
        Assert.Equal(64, Claim.TranscriptSha256(Identity).Length);
        Assert.DoesNotContain("192.168", Identity.ToString());
        Assert.DoesNotContain(Claim.TokenSha256, Claim.ToString());
        var changedEndpoint = new EnrollmentIdentity(IdentityId, "https://192.168.1.11:8443", Identity.SpkiSha256,
            Identity.LeafDer.AsSpan(), Identity.LeafNotBefore, Identity.LeafNotAfter);
        Assert.NotEqual(Claim.TranscriptSha256(Identity), Claim.TranscriptSha256(changedEndpoint));
    }

    [Theory]
    [InlineData("=")]
    [InlineData("+")]
    [InlineData("/")]
    [InlineData("B")]
    public void NoncanonicalNonceCannotEnterTranscript(string suffix) =>
        Assert.Throws<ArgumentException>(() => new EnrollmentClaim(EnrollmentId, PhoneId, new string('A', 42) + suffix, new string('b', 64)));

    [Fact]
    public void MissingOrUncertainStorageRequiresRecoveryWithoutWriting()
    {
        foreach (var outcome in new[] { AuthorizationLoadOutcome.Missing, AuthorizationLoadOutcome.Uncertain, (AuthorizationLoadOutcome)999 })
        {
            var journal = new Journal { LoadOutcome = outcome };
            var subject = new EnrollmentAuthorization(Identity, journal, new Clock());
            Assert.True(subject.RecoveryRequired);
            Assert.False(subject.BeginEnrollmentByLocalOwner(EnrollmentId));
            Assert.Equal(0, journal.Writes);
        }
    }

    [Fact]
    public void OneClaimAndExactOwnerTranscriptAreRequiredBeforeCommit()
    {
        var journal = new Journal(); var subject = new EnrollmentAuthorization(Identity, journal, new Clock());
        Assert.False(subject.TryClaim(Claim));
        Assert.True(subject.BeginEnrollmentByLocalOwner(EnrollmentId));
        Assert.True(subject.TryClaim(Claim));
        Assert.False(subject.TryClaim(Claim));
        Assert.False(subject.ApproveExposureByLocalOwner());
        Assert.False(subject.ConfirmEnrollmentByLocalOwner(new string('0', 64)));
        Assert.False(subject.ConfirmEnrollmentByLocalOwner(Claim.TranscriptSha256(Identity)));
        Assert.Equal(0, journal.Writes);
    }

    [Fact]
    public void IssuanceCommitsFirstAndNeedsSeparateFreshExposureApproval()
    {
        var journal = new Journal(); var subject = Pending(journal, new Clock());
        journal.DuringCommit = count => Assert.False(subject.TryPrepareAuthenticatedResponse(PhoneId, 1, out _));
        Assert.True(subject.ConfirmEnrollmentByLocalOwner(Claim.TranscriptSha256(Identity)));
        Assert.False(subject.TryPrepareAuthenticatedResponse(PhoneId, 1, out _));
        Assert.True(subject.ApproveExposureByLocalOwner());
        Assert.True(subject.TryPrepareAuthenticatedResponse(PhoneId, 1, out var lease));
        Assert.True(subject.RecheckBeforeRelease(lease!));
        Assert.Equal(Identity.LeafNotAfter, journal.Record!.ExpiresAt);
        Assert.Equal(1, journal.Record.Generation);
        Assert.False(subject.TryPrepareAuthenticatedResponse(Guid.Empty, 1, out _));
        Assert.False(subject.TryPrepareAuthenticatedResponse(PhoneId, 2, out _));
    }

    [Theory]
    [InlineData(AuthorizationCommitOutcome.NotCommitted)]
    [InlineData(AuthorizationCommitOutcome.Uncertain)]
    [InlineData((AuthorizationCommitOutcome)999)]
    public void NoncommittedIssuanceLatchesRecoveryWithoutRetry(AuthorizationCommitOutcome outcome)
    {
        var journal = new Journal { CommitOutcome = outcome }; var subject = Pending(journal, new Clock());
        Assert.False(subject.ConfirmEnrollmentByLocalOwner(Claim.TranscriptSha256(Identity)));
        Assert.True(subject.RecoveryRequired);
        journal.CommitOutcome = AuthorizationCommitOutcome.Committed;
        Assert.False(subject.BeginEnrollmentByLocalOwner(EnrollmentId));
        Assert.False(subject.ApproveExposureByLocalOwner());
        Assert.Equal(1, journal.Writes);
    }

    [Fact]
    public void JournalExceptionsFailClosedWithoutLeakingTheException()
    {
        var journal = new Journal { DuringCommit = _ => throw new IOException("synthetic failure") };
        var subject = Pending(journal, new Clock());
        Assert.False(subject.ConfirmEnrollmentByLocalOwner(Claim.TranscriptSha256(Identity)));
        Assert.True(subject.RecoveryRequired);
        Assert.Equal(1, journal.Writes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ClaimWindowExpiresAtBoundaryOnEitherClock(bool advanceWall)
    {
        var clock = new Clock(); var journal = new Journal(); var subject = Pending(journal, clock);
        if (advanceWall) clock.Utc = clock.Utc.AddSeconds(120);
        else clock.Tick += TimeSpan.FromSeconds(120).Ticks;
        Assert.False(subject.ConfirmEnrollmentByLocalOwner(Claim.TranscriptSha256(Identity)));
        Assert.Equal(0, journal.Writes);
    }

    [Fact]
    public void CommitReturningAfterWindowRetiresItsGenerationWithoutActivating()
    {
        var clock = new Clock(); var journal = new Journal(); var subject = Pending(journal, clock);
        journal.DuringCommit = count => { if (count == 1) clock.Advance(TimeSpan.FromSeconds(120)); };
        Assert.False(subject.ConfirmEnrollmentByLocalOwner(Claim.TranscriptSha256(Identity)));
        Assert.True(journal.Record!.Revoked);
        Assert.Equal(2, journal.Record.Generation);
        Assert.Equal(2, journal.Writes); // Issuance then retirement, never an issuance retry.
        Assert.False(subject.ApproveExposureByLocalOwner());
    }

    [Fact]
    public async Task RevocationInvalidatesPreparedLeaseBeforeBlockedPersistenceCompletes()
    {
        var journal = new Journal(); var subject = Active(journal, new Clock());
        Assert.True(subject.TryPrepareAuthenticatedResponse(PhoneId, 1, out var lease));
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        journal.DuringCommit = _ => { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException(); };
        var revocation = Task.Run(subject.RevokeByLocalOwner);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            Assert.False(revocation.IsCompleted);
            Assert.False(subject.RecheckBeforeRelease(lease!));
            Assert.False(subject.TryPrepareAuthenticatedResponse(PhoneId, 1, out _));
        }
        finally { release.Set(); }
        Assert.True(await revocation.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(journal.Record!.Revoked);
        Assert.Equal(2, journal.Record.Generation);
        Assert.False(subject.RecheckBeforeRelease(lease!));
    }

    [Fact]
    public void UncertainRevocationIsNotAcknowledgedAndCannotReenable()
    {
        var journal = new Journal(); var subject = Active(journal, new Clock());
        journal.CommitOutcome = AuthorizationCommitOutcome.Uncertain;
        Assert.False(subject.RevokeByLocalOwner());
        Assert.True(subject.RecoveryRequired);
        Assert.False(subject.ApproveExposureByLocalOwner());
        Assert.False(subject.BeginEnrollmentByLocalOwner(Guid.Parse("44444444-4444-4444-8444-444444444444")));
    }

    [Fact]
    public void RestoredRecordNeverRestoresExposureAndForeignLeasesAreRejected()
    {
        var journal = new Journal(); var first = Active(journal, new Clock());
        Assert.True(first.TryPrepareAuthenticatedResponse(PhoneId, 1, out var lease));
        var restored = new EnrollmentAuthorization(Identity, journal, new Clock());
        Assert.False(restored.TryPrepareAuthenticatedResponse(PhoneId, 1, out _));
        Assert.True(restored.ApproveExposureByLocalOwner());
        Assert.False(restored.RecheckBeforeRelease(lease!));
    }

    [Theory]
    [InlineData("wall")]
    [InlineData("monotonic")]
    public void ClockRegressionLatchesRecovery(string kind)
    {
        var clock = new Clock(); var subject = Active(new Journal(), clock);
        if (kind == "wall") clock.Utc = clock.Utc.AddTicks(-1); else clock.Tick--;
        Assert.False(subject.ApproveExposureByLocalOwner());
        Assert.True(subject.RecoveryRequired);
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.False(subject.ApproveExposureByLocalOwner());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CredentialExpiryRejectsPreparedResponsesAtBoundary(bool advanceWall)
    {
        var clock = new Clock(); var subject = Active(new Journal(), clock);
        Assert.True(subject.TryPrepareAuthenticatedResponse(PhoneId, 1, out var lease));
        if (advanceWall) clock.Utc = Identity.LeafNotAfter;
        else clock.Tick += (Identity.LeafNotAfter - Now).Ticks;
        Assert.False(subject.RecheckBeforeRelease(lease!));
        Assert.False(subject.ApproveExposureByLocalOwner());
    }

    [Fact]
    public void InconsistentAndThrowingLoadsCannotInitializeFreshTrust()
    {
        var committed = new Journal(); _ = Active(committed, new Clock());
        var loads = new AuthorizationLoad?[] { null, new(AuthorizationLoadOutcome.VerifiedRecord, null),
            new(AuthorizationLoadOutcome.VerifiedEmpty, committed.Record) };
        foreach (var load in loads)
        {
            var journal = new Journal { ReadOverride = () => load! };
            var subject = new EnrollmentAuthorization(Identity, journal, new Clock());
            Assert.True(subject.RecoveryRequired);
            Assert.False(subject.BeginEnrollmentByLocalOwner(EnrollmentId));
            Assert.Equal(0, journal.Writes);
        }
        var throwing = new EnrollmentAuthorization(Identity, new Journal { ReadOverride = () => throw new IOException() }, new Clock());
        Assert.True(throwing.RecoveryRequired);
    }

    [Fact]
    public void GenerationExhaustionReservesRetirementAndNeverWraps()
    {
        var old = new AuthorizationRecord(Identity, Guid.Parse("44444444-4444-4444-8444-444444444444"), PhoneId,
            Claim.TokenSha256, Claim.TranscriptSha256(Identity), long.MaxValue - 1, Now, Identity.LeafNotAfter, true);
        var journal = new Journal { Record = old }; var subject = Pending(journal, new Clock());
        Assert.False(subject.ConfirmEnrollmentByLocalOwner(Claim.TranscriptSha256(Identity)));
        Assert.True(subject.RecoveryRequired);
        Assert.Equal(0, journal.Writes);
    }

    [Fact]
    public void RetirementIsIdempotentAndOldEnrollmentIdCannotBeReused()
    {
        var journal = new Journal(); var subject = Active(journal, new Clock());
        Assert.True(subject.RevokeByLocalOwner());
        Assert.True(subject.RevokeByLocalOwner());
        Assert.Equal(2, journal.Writes);
        Assert.False(subject.BeginEnrollmentByLocalOwner(EnrollmentId));
        Assert.True(subject.BeginEnrollmentByLocalOwner(Guid.Parse("44444444-4444-4444-8444-444444444444")));
    }

    [Fact]
    public void DisabledExposureCannotBeRestoredByAnotherExposureCall()
    {
        var subject = Active(new Journal(), new Clock());
        subject.DisableExposureByLocalOwner();
        Assert.False(subject.ApproveExposureByLocalOwner());
        Assert.False(subject.TryPrepareAuthenticatedResponse(PhoneId, 1, out _));
    }

    private static EnrollmentAuthorization Pending(Journal journal, Clock clock)
    {
        var subject = new EnrollmentAuthorization(Identity, journal, clock);
        Assert.True(subject.BeginEnrollmentByLocalOwner(EnrollmentId)); Assert.True(subject.TryClaim(Claim));
        return subject;
    }
    private static EnrollmentAuthorization Active(Journal journal, Clock clock)
    {
        var subject = Pending(journal, clock);
        Assert.True(subject.ConfirmEnrollmentByLocalOwner(Claim.TranscriptSha256(Identity)));
        Assert.True(subject.ApproveExposureByLocalOwner());
        return subject;
    }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Utc = Now; public long Tick = TimeSpan.FromHours(1).Ticks;
        public override DateTimeOffset GetUtcNow() => Utc;
        public override long GetTimestamp() => Tick;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public void Advance(TimeSpan duration) { Utc += duration; Tick += duration.Ticks; }
    }
    private sealed class Journal : IAuthorizationJournal
    {
        public AuthorizationLoadOutcome LoadOutcome = AuthorizationLoadOutcome.VerifiedEmpty;
        public AuthorizationCommitOutcome CommitOutcome = AuthorizationCommitOutcome.Committed;
        public AuthorizationRecord? Record;
        public int Writes;
        public Action<int>? DuringCommit;
        public Func<AuthorizationLoad>? ReadOverride;
        public AuthorizationLoad Read() => ReadOverride is { } read ? read() :
            new(Record is null ? LoadOutcome : AuthorizationLoadOutcome.VerifiedRecord, Record);
        public AuthorizationCommitOutcome Commit(long expectedGeneration, AuthorizationRecord next)
        {
            Writes++; DuringCommit?.Invoke(Writes);
            if (expectedGeneration != (Record?.Generation ?? 0)) return AuthorizationCommitOutcome.NotCommitted;
            if (CommitOutcome == AuthorizationCommitOutcome.Committed) Record = next;
            return CommitOutcome;
        }
    }
}
