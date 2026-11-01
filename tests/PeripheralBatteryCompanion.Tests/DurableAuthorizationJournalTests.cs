using PeripheralBatteryCompanion.Core.TransportSecurity;
using Xunit;

namespace PeripheralBatteryCompanion.Tests;

public sealed class DurableAuthorizationJournalTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
    private static EnrollmentIdentity Identity => new(Guid.Parse("11111111-1111-4111-8111-111111111111"),
        "https://192.168.1.10:8443", new string('a', 64), [1, 2, 3], Now.AddDays(-1), Now.AddDays(89));
    private static EnrollmentClaim Claim => new(Guid.Parse("22222222-2222-4222-8222-222222222222"),
        Guid.Parse("33333333-3333-4333-8333-333333333333"), new string('A', 43), new string('b', 64));
    private static AuthorizationRecord Record => new(Identity, Claim.EnrollmentId, Claim.PhoneId, Claim.TokenSha256,
        Claim.TranscriptSha256(Identity), 1, Now, Now.AddDays(89), false);

    [Fact]
    public void MissingStorageIsNeverAutomaticallyInitialized()
    {
        using var disk = new Disk(); using var journal = disk.Open();
        Assert.Equal(AuthorizationLoadOutcome.Missing, journal.Read().Outcome);
        Assert.False(File.Exists(Path.Join(disk.Root, "state.bin")));
        Assert.False(journal.InitializeFreshByLocalOwner());
    }

    [Fact]
    public void RealFilesRestoreCommittedGenerationAndTombstoneAfterCleanClose()
    {
        using var disk = new Disk();
        using (var journal = disk.Open())
        {
            Assert.True(journal.InitializeFreshByLocalOwner());
            Assert.Equal(AuthorizationLoadOutcome.VerifiedEmpty, journal.Read().Outcome);
            Assert.Equal(AuthorizationCommitOutcome.Committed, journal.Commit(0, Record));
            Assert.True(File.Exists(Path.Join(disk.Root, "session.bin")));
        }
        using (var restored = disk.Open())
        {
            var loaded = restored.Read();
            Assert.Equal(AuthorizationLoadOutcome.VerifiedRecord, loaded.Outcome);
            Assert.Equal(1, loaded.Record!.Generation);
            Assert.Equal(AuthorizationCommitOutcome.Committed, restored.Commit(1, loaded.Record.Tombstone()));
        }
        using var retired = disk.Open();
        Assert.True(retired.Read().Record!.Revoked);
        Assert.Equal(2, retired.Read().Record!.Generation);
        Assert.True(disk.Flushes > 0);
        Assert.True(disk.ValidatedHandles > 0);
    }

    [Fact]
    public void GenerationCompareAndTransitionRulesPreventReplacementOfActivePhone()
    {
        using var disk = new Disk(); using var journal = disk.Open(); Assert.True(journal.InitializeFreshByLocalOwner());
        Assert.Equal(AuthorizationCommitOutcome.NotCommitted, journal.Commit(3, Record));
        Assert.Equal(AuthorizationCommitOutcome.Committed, journal.Commit(0, Record));
        Assert.Equal(AuthorizationCommitOutcome.NotCommitted, journal.Commit(0, Record));
        var replacement = new AuthorizationRecord(Identity, Claim.EnrollmentId, Guid.Parse("44444444-4444-4444-8444-444444444444"),
            Claim.TokenSha256, Claim.TranscriptSha256(Identity), 2, Now, Record.ExpiresAt, false);
        Assert.Equal(AuthorizationCommitOutcome.NotCommitted, journal.Commit(1, replacement));
        Assert.Equal(1, journal.Read().Record!.Generation);
    }

    [Theory]
    [InlineData("create:pending.bin")]
    [InlineData("create:state.next")]
    [InlineData("replace:state.bin")]
    [InlineData("create:checkpoint.next")]
    [InlineData("replace:checkpoint.bin")]
    [InlineData("delete:pending.bin")]
    [InlineData("flush")]
    public void InterruptedTransactionNeverRestoresAsApproved(string operation)
    {
        using var disk = new Disk();
        using (var journal = disk.Open())
        {
            Assert.True(journal.InitializeFreshByLocalOwner());
            disk.FailOnce = operation;
            Assert.Equal(AuthorizationCommitOutcome.Uncertain, journal.Commit(0, Record));
            Assert.Equal(AuthorizationLoadOutcome.Uncertain, journal.Read().Outcome);
        }
        using var restart = disk.Open();
        Assert.Equal(AuthorizationLoadOutcome.Uncertain, restart.Read().Outcome);
        Assert.False(restart.InitializeFreshByLocalOwner());
        Assert.True(File.Exists(Path.Join(disk.Root, "session.bin")));
    }

    [Fact]
    public void SessionMarkerDeniesRestartEvenWhenStopMarkerCannotBeWritten()
    {
        using var disk = new Disk();
        using (var journal = disk.Open())
        {
            Assert.True(journal.InitializeFreshByLocalOwner());
            disk.DenyStop = true; disk.FailOnce = "replace:checkpoint.bin";
            Assert.Equal(AuthorizationCommitOutcome.Uncertain, journal.Commit(0, Record));
            Assert.False(File.Exists(Path.Join(disk.Root, "stop.bin")));
        }
        using var restart = disk.Open();
        Assert.Equal(AuthorizationLoadOutcome.Uncertain, restart.Read().Outcome);
    }

    [Fact]
    public void AbandonedSessionMarkerIsNotSilentlyClearedOnRestart()
    {
        using var disk = new Disk(); var abandoned = disk.Open();
        Assert.True(abandoned.InitializeFreshByLocalOwner());
        Assert.Equal(AuthorizationCommitOutcome.Committed, abandoned.Commit(0, Record));
        disk.DropLease(); // Managed interruption simulation, not an OS crash/power-loss claim.
        using var restart = disk.Open();
        Assert.Equal(AuthorizationLoadOutcome.Uncertain, restart.Read().Outcome);
        Assert.True(File.Exists(Path.Join(disk.Root, "session.bin")));
    }

    [Theory]
    [InlineData("checkpoint.bin")]
    [InlineData("initialized.bin")]
    [InlineData("state.bin")]
    public void CorruptOrMissingCommittedFileIsDeniedWithoutFallback(string file)
    {
        using var disk = new Disk();
        using (var journal = disk.Open()) { Assert.True(journal.InitializeFreshByLocalOwner()); Assert.Equal(AuthorizationCommitOutcome.Committed, journal.Commit(0, Record)); }
        File.WriteAllBytes(Path.Join(disk.Root, file), [0xFF]);
        using var restart = disk.Open();
        Assert.Equal(AuthorizationLoadOutcome.Uncertain, restart.Read().Outcome);
        Assert.True(File.Exists(Path.Join(disk.Root, "stop.bin")));
    }

    [Fact]
    public void UnknownFilesAndOversizeStateAreRejectedWithoutDeletingEvidence()
    {
        using var disk = new Disk();
        using (var journal = disk.Open()) Assert.True(journal.InitializeFreshByLocalOwner());
        string unknown = Path.Join(disk.Root, "unowned.txt"); File.WriteAllText(unknown, "synthetic");
        using (var restart = disk.Open()) Assert.Equal(AuthorizationLoadOutcome.Uncertain, restart.Read().Outcome);
        Assert.True(File.Exists(unknown));
        using var largeDisk = new Disk();
        using (var journal = largeDisk.Open()) Assert.True(journal.InitializeFreshByLocalOwner());
        File.WriteAllBytes(Path.Join(largeDisk.Root, "state.bin"), new byte[DurableAuthorizationJournal.MaximumCiphertextBytes + 1]);
        using var large = largeDisk.Open(); Assert.Equal(AuthorizationLoadOutcome.Uncertain, large.Read().Outcome);
    }

    [Fact]
    public void ExclusiveLeaseDeniesAConcurrentJournalWithoutChangingOwnersRecord()
    {
        using var disk = new Disk(); using var first = disk.Open(); Assert.True(first.InitializeFreshByLocalOwner());
        using var contender = disk.Open();
        Assert.Equal(AuthorizationLoadOutcome.Uncertain, contender.Read().Outcome);
        Assert.Equal(AuthorizationLoadOutcome.VerifiedEmpty, first.Read().Outcome);
        Assert.Equal(AuthorizationCommitOutcome.Committed, first.Commit(0, Record));
    }

    [Fact]
    public void CoordinatorUsesRealJournalAndClosesSessionWithoutRestoringExposure()
    {
        using var disk = new Disk(); var journal = disk.Open(); Assert.True(journal.InitializeFreshByLocalOwner());
        using (var coordinator = new EnrollmentAuthorization(Identity, journal, new Clock()))
        {
            Assert.True(coordinator.BeginEnrollmentByLocalOwner(Claim.EnrollmentId)); Assert.True(coordinator.TryClaim(Claim));
            Assert.True(coordinator.ConfirmEnrollmentByLocalOwner(Claim.TranscriptSha256(Identity)));
            Assert.True(coordinator.ApproveExposureByLocalOwner()); Assert.True(coordinator.RevokeByLocalOwner());
        }
        Assert.False(File.Exists(Path.Join(disk.Root, "session.bin")));
        var restored = disk.Open();
        using var restarted = new EnrollmentAuthorization(Identity, restored, new Clock());
        Assert.False(restarted.RecoveryRequired);
        Assert.False(restarted.ApproveExposureByLocalOwner());
        Assert.True(restored.Read().Record!.Revoked);
    }

    [Fact]
    public void CoordinatorClockFailurePersistsStopBeforeCleanDisposalCanClearSession()
    {
        using var disk = new Disk(); var journal = disk.Open(); Assert.True(journal.InitializeFreshByLocalOwner());
        var clock = new Clock();
        using (var coordinator = new EnrollmentAuthorization(Identity, journal, clock))
        {
            clock.Utc = Now.AddSeconds(-1);
            Assert.False(coordinator.BeginEnrollmentByLocalOwner(Claim.EnrollmentId));
            Assert.True(coordinator.RecoveryRequired);
        }
        using var restart = disk.Open(); Assert.Equal(AuthorizationLoadOutcome.Uncertain, restart.Read().Outcome);
    }

    [Fact]
    public void ClockFailureBeforeJournalReadStillMarksExistingStorageUncertain()
    {
        using var disk = new Disk(); using (var initial = disk.Open()) Assert.True(initial.InitializeFreshByLocalOwner());
        using (var coordinator = new EnrollmentAuthorization(Identity, disk.Open(), new InvalidClock()))
            Assert.True(coordinator.RecoveryRequired);
        using var restart = disk.Open(); Assert.Equal(AuthorizationLoadOutcome.Uncertain, restart.Read().Outcome);
    }

    [Fact]
    public void PartialRollbackOfStateAgainstNewerTombstoneCheckpointIsDenied()
    {
        using var disk = new Disk(); byte[] oldState;
        using (var journal = disk.Open())
        {
            Assert.True(journal.InitializeFreshByLocalOwner()); Assert.Equal(AuthorizationCommitOutcome.Committed, journal.Commit(0, Record));
            oldState = File.ReadAllBytes(Path.Join(disk.Root, "state.bin"));
            Assert.Equal(AuthorizationCommitOutcome.Committed, journal.Commit(1, Record.Tombstone()));
        }
        File.WriteAllBytes(Path.Join(disk.Root, "state.bin"), oldState);
        using var restart = disk.Open(); Assert.Equal(AuthorizationLoadOutcome.Uncertain, restart.Read().Outcome);
        // Restoring EVERY old file consistently is a different, explicitly unproven threat.
    }

    [Fact]
    public void FailedCleanCloseIsReportedAndRetainsRestartDenial()
    {
        using var disk = new Disk(); var journal = disk.Open(); Assert.True(journal.InitializeFreshByLocalOwner());
        disk.FailOnce = "delete:session.bin";
        Assert.Throws<IOException>(journal.Dispose);
        using var restart = disk.Open(); Assert.Equal(AuthorizationLoadOutcome.Uncertain, restart.Read().Outcome);
    }

    private sealed class InvalidClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now.ToOffset(TimeSpan.FromHours(1));
    }

    // Real FileStreams/rename/Flush(true), with synthetic public record data only.
    // Protection is deliberately a TEST wrapper, NOT encryption; ACL and directory
    // durability callbacks here do NOT attest the held Windows production backend.
    private sealed class TestProtection : IAuthorizationEnvelopeProtection
    {
        public byte[] Protect(byte[] plaintext) => new byte[] { 0x54 }.Concat(plaintext).ToArray();
        public byte[] Unprotect(byte[] ciphertext) => ciphertext[0] == 0x54 ? ciphertext[1..] : throw new IOException();
    }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Utc = Now;
        public override DateTimeOffset GetUtcNow() => Utc;
        public override long GetTimestamp() => TimeSpan.FromHours(1).Ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }
    private sealed class Disk : IAuthorizationJournalStorage, IDisposable
    {
        public string Root { get; } = Path.Join(Path.GetTempPath(), "PBC-JournalTests-" + Guid.NewGuid().ToString("N"));
        public int Flushes, ValidatedHandles;
        public string? FailOnce;
        public bool DenyStop;
        private IAuthorizationJournalFiles? _latest;
        private readonly List<IAuthorizationJournalFiles> _sessions = [];
        public Disk() => Directory.CreateDirectory(Root);
        public DurableAuthorizationJournal Open() => new(Identity, this, new TestProtection());
        public IAuthorizationJournalFiles AcquireExclusive()
        {
            var store = new AuthorizationJournalFileStorage(Root, (handle, path) =>
            {
                Assert.False(handle.IsInvalid); Assert.Equal(Root, Path.GetDirectoryName(path)); ValidatedHandles++;
            }, () => Flushes++, (path, mode, access, share, options) => new FileStream(path, mode, access, share, 4096, options));
            _latest = new FaultFiles(store.AcquireExclusive(), this); _sessions.Add(_latest); return _latest;
        }
        public void DropLease() => _latest!.Dispose();
        private void Before(string operation)
        {
            if (DenyStop && operation == "create:stop.bin") throw new IOException("synthetic stop write denied");
            if (FailOnce == operation) { FailOnce = null; throw new IOException("synthetic I/O failure"); }
        }
        public void Dispose()
        {
            foreach (var session in _sessions) session.Dispose();
            string expectedPrefix = Path.Join(Path.GetTempPath(), "PBC-JournalTests-");
            if (!Path.GetFullPath(Root).StartsWith(expectedPrefix, StringComparison.Ordinal) ||
                new DirectoryInfo(Root).Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Test cleanup target rejected.");
            Directory.Delete(Root, recursive: true);
        }
        private sealed class FaultFiles(IAuthorizationJournalFiles inner, Disk disk) : IAuthorizationJournalFiles
        {
            public IReadOnlyList<string> Inventory() => inner.Inventory();
            public byte[]? Read(string name, int maximumBytes) => inner.Read(name, maximumBytes);
            public void CreateNewAndFlush(string name, byte[] bytes) { disk.Before("create:" + name); inner.CreateNewAndFlush(name, bytes); }
            public void Replace(string temporaryName, string destinationName) { disk.Before("replace:" + destinationName); inner.Replace(temporaryName, destinationName); }
            public void DeleteExisting(string name) { disk.Before("delete:" + name); inner.DeleteExisting(name); }
            public void FlushDirectory() { disk.Before("flush"); inner.FlushDirectory(); }
            public void Dispose() => inner.Dispose();
        }
    }
}
