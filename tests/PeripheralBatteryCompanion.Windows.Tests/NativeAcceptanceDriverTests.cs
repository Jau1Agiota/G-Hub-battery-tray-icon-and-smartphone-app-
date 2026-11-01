extern alias acceptance;
using System.Text;
using System.Text.Json;
using A = acceptance::PeripheralBatteryCompanion.NativeAcceptance;
using W = acceptance::PeripheralBatteryCompanion.Windows;
using Xunit;

namespace PeripheralBatteryCompanion.Windows.Tests;

public sealed class NativeAcceptanceDriverTests
{
    private static A.ExecutionManifest Manifest() => new(1, "11111111-1111-4111-8111-111111111111", "22222222-2222-4222-8222-222222222222",
        "S-1-5-21-100-200-300-1001", @"C:\Fixture\LocalAppData", @"C:\Fixture\bin\GateE.NativeAcceptance.exe", new string('a', 40),
        A.AcceptancePlan.RequiredArtifacts
            .ToDictionary(n => n, _ => new string('a', 64)), "2026-11-01T12:00:00Z", "2026-11-01T12:30:00Z", "2026-11-01T11:00:00Z", "2026-11-02T11:00:00Z");
    [Fact]
    public void ManifestHasClosedPlanAndGlobalBudgets()
    {
        var m = A.ExecutionManifest.Parse(JsonSerializer.SerializeToUtf8Bytes(Manifest()));
        Assert.Equal(23, A.AcceptancePlan.Cases.Length); Assert.Equal(6, A.AcceptancePlan.Cases.Count(c => c.CrashPhase is not null));
        Assert.True(A.AcceptancePlan.Cases.Sum(c => c.Dpapi) <= 128); Assert.True(A.AcceptancePlan.Cases.Sum(c => c.Signatures) <= 8);
        Assert.True(A.AcceptancePlan.Cases.Sum(c => c.Verifications) <= 16);
        Assert.Equal(m.Root + @"\clean", m.Owned("clean"));
    }
    [Theory]
    [InlineData("../outside")][InlineData("C:\\outside")][InlineData("clean:ads")][InlineData("clean//other")][InlineData("clean/../outside")]
    public void ManifestNeverResolvesUnownedPaths(string path) => Assert.Throws<InvalidDataException>(() => Manifest().Owned(path));
    [Theory]
    [InlineData("duplicate")][InlineData("extra")][InlineData("oversize")][InlineData("sid")][InlineData("unc")][InlineData("artifact")][InlineData("time")]
    public void InvalidManifestCannotReachAdmission(string variant)
    {
        var m = Manifest();
        if (variant == "sid") m = m with { AccountSid = "S-1-5-18" };
        if (variant == "unc") m = m with { LocalAppData = @"\\server\share" };
        if (variant == "artifact") m = m with { Artifacts = new() { ["arbitrary.exe"] = new string('a', 64) } };
        if (variant == "time") m = m with { ExpiresUtc = "2026-11-02T12:30:00Z" };
        string json = JsonSerializer.Serialize(m);
        if (variant == "duplicate") json = json.Insert(1, "\"Schema\":1,");
        if (variant == "extra") json = json.Insert(1, "\"Enable\":true,");
        if (variant == "oversize") json = new string(' ', 16385);
        Assert.ThrowsAny<Exception>(() => A.ExecutionManifest.Parse(Encoding.UTF8.GetBytes(json)));
    }
    [Theory]
    [InlineData("dpapi", 2)][InlineData("sign", 1)][InlineData("verify", 1)][InlineData("nonce", 8)][InlineData("bootstrap", 1)][InlineData("credential", 1)][InlineData("key", 1)]
    public void ExhaustedBudgetCannotBeReused(string operation, int limit)
    {
        var budget = new A.OperationBudget(new("test", "", 2, 1, 1));
        for (int i = 0; i < limit; i++) budget.Charge(operation);
        Assert.Throws<InvalidOperationException>(() => budget.Charge(operation));
        Assert.Throws<InvalidOperationException>(() => budget.Charge(operation));
    }
    [Fact]
    public void NoNativeEntryOrLinkedPrimitiveAdmitsWithoutAuthorization()
    {
        Assert.Equal(77, A.Program.Main(["--manifest", @"C:\must-not-read.json"]));
        Assert.Throws<InvalidOperationException>(() => A.NativeAdmission.Enter(null!, null!));
        Assert.Throws<InvalidOperationException>(() => new W.WindowsCurrentUserRecordProtection().ProtectJournalEnvelope(null!, null!));
        Assert.Throws<InvalidOperationException>(() => W.WindowsPersistentIdentityCustody.CreateByLocalOwner(Guid.Empty));
        Assert.Throws<InvalidOperationException>(() => W.WindowsAuthorizationDirectory.CreateFresh(null!));
        Assert.Throws<InvalidOperationException>(() => new W.WindowsProtocolEntropy().NewRequestNonce());
        Assert.Throws<InvalidOperationException>(() => new W.WindowsEnrolledCertificateSignature().Verify(default, default, default));
    }
    [Fact]
    public async Task ManagedProcessModelObservesEachPhaseAndRetainsFaultEvidence()
    {
        var launcher = new FakeLauncher(); var report = await A.ManagedDriver.Run(launcher, 42);
        Assert.Equal(23, report.Cases.Count); Assert.Equal(6, launcher.Terminations);
        Assert.Equal(A.Verdict.NotRun, report.Cases.Single(c => c.Case == "cleanup").Outcome);
        Assert.All(report.Cases.Where(c => c.Case != "cleanup"), c => { Assert.Equal(A.Verdict.Passed, c.Outcome); Assert.True(c.ExitConfirmed); });
        Assert.DoesNotContain("SECRET", Encoding.UTF8.GetString(report.Bytes()));
    }
    [Theory]
    [InlineData("parent")][InlineData("unknown-exit")][InlineData("malformed")][InlineData("timeout")]
    public async Task UnsafeProcessOutcomeStopsPlanAndNeverDispatchesCleanup(string failure)
    {
        var launcher = new FakeLauncher(failure); var report = await A.ManagedDriver.Run(launcher, 42, TimeSpan.FromMilliseconds(30));
        Assert.Equal(1, launcher.Starts); Assert.Equal(A.Verdict.Uncertain, report.Cases[0].Outcome);
        Assert.All(report.Cases.Skip(1), c => Assert.Equal(A.Verdict.NotRun, c.Outcome));
        if (failure == "parent") Assert.Equal(0, launcher.Terminations);
    }
    [Fact]
    public void EvidenceRejectsUnknownOrRepeatedCaseAndNeverAcceptsRawErrorText()
    {
        var evidence = new A.EvidenceLog(); evidence.Add(new("prepare", A.Verdict.Uncertain, ErrorCode: -1));
        Assert.Throws<InvalidDataException>(() => evidence.Add(new("prepare", A.Verdict.Passed)));
        Assert.Throws<InvalidOperationException>(() => evidence.Add(new("SECRET", A.Verdict.Failed)));
        Assert.Throws<InvalidDataException>(() => new A.EvidenceLog().Add(new("prepare", A.Verdict.Failed, ErrorKind: "SECRET")));
        Assert.True(evidence.Bytes().Length < 1024 * 1024);
    }
    [Fact]
    public void ChildFailureCodesAreTypedAndNeverAcceptRawDiagnostics()
    {
        Assert.Equal((5, "Win32"), A.ManagedDriver.ParseFailure("ERROR:Win32:5"));
        Assert.Equal((-1, "HResult"), A.ManagedDriver.ParseFailure("ERROR:HResult:-1"));
        Assert.Null(A.ManagedDriver.ParseFailure("ERROR:Win32:005").Code);
        Assert.Null(A.ManagedDriver.ParseFailure("SECRET/path/provider error").Code);
    }
    [Fact]
    public void ProductionAssemblyDoesNotReferenceAcceptanceDriver()
    {
        Assert.DoesNotContain(typeof(PeripheralBatteryCompanion.Windows.WindowsProtocolEntropy).Assembly.GetReferencedAssemblies(),
            name => name.Name == "GateE.NativeAcceptance");
    }
    [Fact]
    public void FailureSeamsAreBoundedAndPreserveStorageCalls()
    {
        var files = new MemoryFiles(); var phases = new List<string>();
        var storage = new A.JournalSeams(new MemoryStorage(files), phases.Add, "write"); var seam = storage.AcquireExclusive();
        Assert.False(storage.InjectionFired);
        Assert.Throws<IOException>(() => seam.CreateNewAndFlush("state.next", [1]));
        Assert.True(storage.InjectionFired);
        seam.CreateNewAndFlush("initialized.bin", [1]); Assert.Empty(phases); seam.FlushDirectory();
        Assert.Equal(new[] { "witness" }, phases); Assert.Equal(1, files.Writes);
        seam.Replace("state.next", "state.bin"); Assert.Equal("state", phases[^1]);
    }
    [Fact]
    public void UnconfirmedNativeCleanupIsNeverAnExpectedDenialPass()
    {
        Assert.Throws<InvalidOperationException>(() => A.NativeCases.Denies(() => throw new W.NativeRecordProtectionFailure(true, true)));
        Assert.Throws<InvalidOperationException>(() => A.NativeCases.Denies(() => throw new W.NativeDirectoryValidationFailure(true)));
        A.NativeCases.Denies(() => throw new W.NativeRecordProtectionFailure(true, false, 13));
        A.NativeCases.Denies(() => throw new W.NativeDirectoryValidationFailure(false));
    }
    private sealed class FakeLauncher(string? failure = null) : A.ICaseLauncher
    {
        internal int Starts, Terminations;
        public A.IOwnedChild Start(A.CasePlan plan) { Starts++; return new FakeChild(this, plan, Starts == 1 ? failure : null); }
    }
    private sealed class FakeChild(FakeLauncher owner, A.CasePlan plan, string? failure) : A.IOwnedChild
    {
        public A.ProcessObservation Observation => new(100, failure == "parent" ? 99 : 42, 123);
        public async Task<string?> ReadLine(CancellationToken stop)
        {
            if (failure == "timeout") await Task.Delay(Timeout.Infinite, stop);
            if (failure == "malformed") throw new InvalidDataException("SECRET raw input must not leak");
            return plan.CrashPhase is not null ? "READY:" + plan.CrashPhase : "RESULT:Passed";
        }
        public void TerminateOwned() => owner.Terminations++;
        public Task<bool> WaitStopped(TimeSpan timeout) => Task.FromResult(failure != "unknown-exit");
        public int ExitCode => 0; public long ExitUtcTicks => 456; public void Dispose() { }
    }
    private sealed class MemoryStorage(MemoryFiles files) : PeripheralBatteryCompanion.Core.TransportSecurity.IAuthorizationJournalStorage
    { public PeripheralBatteryCompanion.Core.TransportSecurity.IAuthorizationJournalFiles AcquireExclusive() => files; }
    private sealed class MemoryFiles : PeripheralBatteryCompanion.Core.TransportSecurity.IAuthorizationJournalFiles
    {
        internal int Writes;
        public IReadOnlyList<string> Inventory() => [];
        public byte[]? Read(string name, int maximumBytes) => null;
        public void CreateNewAndFlush(string name, byte[] bytes) => Writes++;
        public void Replace(string a, string b) { }
        public void DeleteExisting(string name) { }
        public void FlushDirectory() { }
        public void Dispose() { }
    }
}
