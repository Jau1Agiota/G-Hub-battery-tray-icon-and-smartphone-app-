using System.Buffers.Binary;
using PeripheralBatteryCompanion.EphemeralCapability;
using Xunit;

namespace PeripheralBatteryCompanion.Tests;

// Entire Option2Engine call flow. Synthetic handles, journal and bytes only: no files/native crypto created.
public sealed class Option2HarnessTests
{
    private const string Id = "1234567812344234a2341234567890ab";
    private static O2Permit Permit() => O2Permit.Create(O2Result.Contract, Id, O2Permit.Confirmation, O2Permit.UiAcknowledgement)!;
    private static O2Result Run(World world, Func<int, byte[]>? allocate = null) => Option2Engine.Run(Permit(), world, world, allocate);

    [Theory]
    [InlineData("create")] [InlineData("set-KeyExportPolicy")] [InlineData("set-KeyUsage")]
    [InlineData("ui-set")] [InlineData("finalize")] [InlineData("public-spki")]
    public void ErrorAfterPossibleNativeSideEffectPreservesOrphanWithoutRecovery(string edge)
    {
        var world = new World { ThrowAt = edge, CreateSideEffectFirst = true };
        var result = Run(world);
        Assert.True(world.PersistentKey);
        Assert.Equal(O2OwnershipState.Unknown, result.Ownership);
        Assert.Equal(O2Cleanup.OrphanUnconfirmed, result.Cleanup);
        Assert.Equal(0, world.DeleteCount);
        Assert.DoesNotContain("reopen", world.Log);
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void UnusableCreatedHandleCannotBecomeBindingProof(int state)
    {
        var world = new World(); world.Original.State = (O2HandleState)state;
        var result = Run(world);
        Assert.Equal(O2OwnershipState.Unknown, result.Ownership);
        Assert.Equal(0, world.DeleteCount);
        Assert.DoesNotContain("finalize", world.Log);
        Assert.DoesNotContain("reopen", world.Log);
    }

    [Fact]
    public void FlushedBindingWithoutIndependentVerificationNeverAuthorizesCleanup()
    {
        var world = new World { InvalidJournalVerify = 1 };
        var result = Run(world);
        Assert.Equal(O2Stage.Bound, world.Journal.Stage);
        Assert.Equal(O2OwnershipState.Unknown, result.Ownership);
        Assert.Equal(0, world.DeleteCount); Assert.DoesNotContain("reopen", world.Log);
    }

    [Fact]
    public void ExactOrderedTargetFlowReadsActualZeroProvesPossessionAndConsumesDeleteHandle()
    {
        var world = new World(); var result = Run(world);
        Assert.Equal(Outcome.Inconclusive, result.Result); Assert.Equal("NO-GO", result.PolicyProof);
        Assert.Equal(O2Cleanup.ExactDeleteAndJournalConfirmed, result.Cleanup);
        Assert.Equal(MemoryOutcome.NotAllocated, result.Memory);
        Assert.Equal(new[] { (MetadataProperty.KeyExportPolicy, 0u, 0), (MetadataProperty.KeyUsage, 2u, 0) }, world.Sets);
        Assert.Equal((1u, 0u, 0), world.UiSet);
        Assert.Equal(O2Plan.Provider, world.ProviderName); Assert.Equal(O2Plan.Algorithm, world.Algorithm);
        Assert.Equal(new O2Plan(Id).KeyName, world.CreatedName);
        Assert.Equal(0, world.CreateFlags); Assert.Equal(0, world.CreateLegacy); Assert.Equal(0, world.FinalizeFlags);
        Assert.True(world.Log.IndexOf("set-KeyExportPolicy") < world.Log.IndexOf("finalize"));
        Assert.True(world.Log.IndexOf("set-KeyUsage") < world.Log.IndexOf("finalize"));
        Assert.True(world.Log.IndexOf("reopen") < world.Log.IndexOf("get-KeyExportPolicy"));
        Assert.True(world.Log.IndexOf("verify-signature") < world.Log.IndexOf("size"));
        Assert.Equal(1, world.Log.Count(s => s == "finalize")); Assert.Equal(1, world.Log.Count(s => s == "reopen"));
        Assert.Equal(1, world.Log.Count(s => s == "size")); Assert.DoesNotContain("fill", world.Log);
        Assert.Equal(1, world.DeleteCount); Assert.False(world.PersistentKey);
        Assert.Equal(1, world.Original.FreeCount); Assert.Equal(0, world.Reopened.FreeCount);
        Assert.Equal(O2HandleState.ConsumedByDelete, world.Reopened.State);
        Assert.Equal(1, world.Provider.FreeCount); Assert.Equal(1, world.Journal.CloseCount);
        Assert.Equal(O2Stage.Completed, world.Journal.Stage);
        Assert.Contains(result.Evidence, e => e.Phase == O2Phase.Sizing && e.Category == "ExportRejectionObservedCauseUnestablished");
        Assert.Contains(result.Evidence, e => e.Phase == O2Phase.ReleaseReopened && e.Category.Contains("ConsumedBySuccessfulDelete"));
    }

    [Theory]
    [InlineData(unchecked((int)0x80090029))]
    [InlineData(unchecked((int)0x8009000A))]
    [InlineData(unchecked((int)0x8009000B))]
    [InlineData(unchecked((int)0x80090010))]
    public void GenericExportRejectionIsNeverCausalPolicyPass(int code)
    {
        var world = new World { SizeCode = code }; var result = Run(world);
        Assert.Equal(Outcome.Inconclusive, result.Result); Assert.Equal("NO-GO", result.PolicyProof);
        Assert.Contains(result.Evidence, e => e.Phase == O2Phase.Sizing && e.HResult == code);
        Assert.DoesNotContain("fill", world.Log); Assert.Equal(1, world.DeleteCount);
    }

    [Fact]
    public void PrivateExportSuccessIsFailureAndFullCapacityIsClearedBeforeDelete()
    {
        var world = new World { SizeCode = null }; byte[]? output = null;
        world.BeforeDelete = () => Assert.All(output!, b => Assert.Equal(0, b));
        var result = Run(world, n => output = new byte[n]);
        Assert.Equal(Outcome.Fail, result.Result); Assert.Equal(MemoryOutcome.ClearedFullCapacity, result.Memory);
        Assert.All(output!, b => Assert.Equal(0, b)); Assert.Equal(1, world.DeleteCount);
        Assert.Equal(1, world.Log.Count(s => s == "fill"));
        Assert.Contains(result.Evidence, e => e.Category == "PrivateExportReturnedNonexportabilityClaimFailed");
        Assert.Equal("NO-GO", result.PolicyProof);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void FillRejectionWithPartialOutputFailsButWithoutOutputRemainsInconclusive(bool partial)
    {
        var world = new World { SizeCode = null, FillCode = unchecked((int)0x80090029), WritePartial = partial }; byte[]? output = null;
        var result = Run(world, n => output = new byte[n]);
        Assert.Equal(partial ? Outcome.Fail : Outcome.Inconclusive, result.Result);
        Assert.Equal(MemoryOutcome.ClearedFullCapacity, result.Memory); Assert.All(output!, b => Assert.Equal(0, b));
        Assert.Equal(1, world.Log.Count(s => s == "size")); Assert.Equal(1, world.Log.Count(s => s == "fill"));
        Assert.Equal(1, world.DeleteCount);
    }

    [Theory]
    [InlineData(-1)] [InlineData(0)] [InlineData(103)] [InlineData(105)] [InlineData(int.MaxValue)]
    public void InvalidSizingNeverAllocatesOrFills(int size)
    {
        var world = new World { SizeCode = null, Size = size };
        var result = Run(world, _ => throw new Exception("must not allocate"));
        Assert.Equal(Outcome.Fail, result.Result); Assert.Equal(MemoryOutcome.NotAllocated, result.Memory);
        Assert.DoesNotContain("fill", world.Log); Assert.Equal(1, world.DeleteCount);
    }

    [Theory]
    [InlineData(-1)] [InlineData(0)] [InlineData(103)] [InlineData(105)] [InlineData(int.MaxValue)]
    public void InvalidFillLengthsStillFailAndClear(int size)
    {
        var world = new World { SizeCode = null, Filled = size }; byte[]? output = null;
        Assert.Equal(Outcome.Fail, Run(world, n => output = new byte[n]).Result);
        Assert.All(output!, b => Assert.Equal(0, b)); Assert.Equal(1, world.DeleteCount);
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("provider")]
    [InlineData("preflight")]
    [InlineData("create")]
    [InlineData("set-KeyExportPolicy")]
    [InlineData("set-KeyUsage")]
    [InlineData("ui-set")]
    [InlineData("finalize")]
    [InlineData("reopen")]
    [InlineData("get-KeyExportPolicy")]
    [InlineData("sign")]
    [InlineData("fill")]
    public void ManagedFailureIsRedactedAndDoesNotInventOwnershipOrRetry(string stage)
    {
        var world = new World { ThrowAt = stage, SizeCode = stage == "fill" ? null : unchecked((int)0x80090029) };
        var result = Run(world);
        Assert.Equal(Outcome.Fail, result.Result);
        Assert.DoesNotContain("SECRET", string.Join("\n", O2Presentation.Format(result)));
        Assert.True(world.DeleteCount <= 1); Assert.True(world.Original.FreeCount <= 1); Assert.True(world.Reopened.FreeCount <= 1);
        if (stage is "create" or "set-KeyExportPolicy" or "set-KeyUsage" or "ui-set" or "finalize" or "reopen") Assert.Equal(0, world.DeleteCount);
        if (stage is "set-KeyExportPolicy" or "set-KeyUsage" or "ui-set") Assert.DoesNotContain("finalize", world.Log);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public void BadRequiredPolicyAlgorithmLengthUsageOrUiPreventsExport(int field)
    {
        var world = new World { WrongMetadata = field }; var result = Run(world);
        Assert.Equal(Outcome.Fail, result.Result); Assert.DoesNotContain("size", world.Log);
        Assert.Equal(1, world.DeleteCount); Assert.Equal(O2Cleanup.ExactDeleteAndJournalConfirmed, result.Cleanup);
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void PublicBindingMismatchNeverDeletesUnrelatedIdentity(int read)
    {
        var world = new World { BadBindingRead = read }; var result = Run(world);
        Assert.Equal(Outcome.Fail, result.Result); Assert.Equal(0, world.DeleteCount);
        Assert.NotEqual(O2Cleanup.ExactDeleteAndJournalConfirmed, result.Cleanup);
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void JournalTamperReparseOrAclFailureNeverAllowsDelete(int verify)
    {
        var world = new World { InvalidJournalVerify = verify }; var result = Run(world);
        Assert.Equal(Outcome.Fail, result.Result); Assert.Equal(0, world.DeleteCount);
        Assert.NotEqual(O2Cleanup.ExactDeleteAndJournalConfirmed, result.Cleanup);
        if (verify < 3) Assert.DoesNotContain("size", world.Log);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void CollisionBeforeCreationNeverAdoptsOrDeletes(bool key)
    {
        var world = new World { ExistingKey = key, JournalCreationFailure = !key }; var result = Run(world);
        Assert.Equal(Outcome.Fail, result.Result); Assert.Equal(0, world.DeleteCount);
        Assert.DoesNotContain("create", world.Log); Assert.Equal(O2Cleanup.NoKeyCreationAttempted, result.Cleanup);
    }

    [Theory]
    [InlineData(0, false)] [InlineData(0, true)]
    [InlineData(1, false)] [InlineData(1, true)]
    [InlineData(2, false)] [InlineData(2, true)]
    [InlineData(3, false)] [InlineData(3, true)]
    [InlineData(4, false)] [InlineData(4, true)]
    [InlineData(5, false)] [InlineData(5, true)]
    [InlineData(6, false)] [InlineData(6, true)]
    [InlineData(7, false)] [InlineData(7, true)]
    [InlineData(8, false)] [InlineData(8, true)]
    [InlineData(9, false)] [InlineData(9, true)]
    [InlineData(10, false)] [InlineData(10, true)]
    [InlineData(11, false)] [InlineData(11, true)]
    [InlineData(13, false)] [InlineData(13, true)]
    public void InterruptedJournalBeforeOrAfterFlushNeverRestoresOrSilentlyAdopts(int stage, bool after)
    {
        var world = new World { InterruptedStage = (O2Stage)stage, InterruptAfterFlush = after,
            SizeCode = (O2Stage)stage == O2Stage.UnexpectedOutput ? null : unchecked((int)0x80090029) };
        var result = Run(world);
        Assert.Equal(Outcome.Fail, result.Result); Assert.NotEqual(O2Cleanup.ExactDeleteAndJournalConfirmed, result.Cleanup);
        Assert.True(world.DeleteCount <= 1);
        if ((O2Stage)stage is O2Stage.Deleted or O2Stage.Completed) {
            Assert.Equal(1, world.DeleteCount); Assert.Equal(0, world.Reopened.FreeCount);
            Assert.Equal(O2HandleState.ConsumedByDelete, world.Reopened.State);
        } else Assert.Equal(0, world.DeleteCount);
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void DeleteFailureUncertaintyAndInconsistentSuccessNeverDoubleFree(int mode)
    {
        var world = new World { DeleteMode = mode }; var result = Run(world);
        Assert.Equal(Outcome.Fail, result.Result); Assert.Equal(O2Cleanup.Unconfirmed, result.Cleanup);
        Assert.Equal(1, world.DeleteCount);
        Assert.Equal(mode is 1 or 3 ? 1 : 0, world.Reopened.FreeCount);
        Assert.Equal(1, world.Provider.FreeCount);
        if (mode == 1) Assert.Contains(result.Evidence, e => e.Phase == O2Phase.Delete && e.HResult == unchecked((int)0x80090010));
    }

    [Theory]
    [InlineData("original")] [InlineData("provider")] [InlineData("journal")]
    public void ReleaseUncertaintyCannotBeHidden(string resource)
    {
        var world = new World { FailedRelease = resource }; var result = Run(world);
        Assert.Equal(Outcome.Fail, result.Result); Assert.Equal(1, world.Original.CloseCount);
        if (resource == "original") { Assert.Equal(0, world.DeleteCount); Assert.DoesNotContain("reopen", world.Log); }
    }

    [Fact]
    public void WrongOwnerAndBadConsentRetiredAndReplayAreBeforeAnyMutation()
    {
        foreach (var id in new string?[] { null, "00000000000000000000000000000000", "123456781234123412341234567890ab",
            "3c4a7c190ef34f2e90625276aa9cbfdb", "3bb5de0629d640569843367dd3407fc8", "6df3c74dc1804991a04730bd39aa519d",
            "6DF3C74DC1804991A04730BD39AA519D" }) Assert.Null(O2Permit.Create(O2Result.Contract, id, O2Permit.Confirmation, O2Permit.UiAcknowledgement));
        Assert.Null(O2Permit.Create(BResult.Contract, Id, O2Permit.Confirmation, O2Permit.UiAcknowledgement));
        Assert.Null(O2Permit.Create(O2Result.Contract, Id, BPermit.Confirmation, O2Permit.UiAcknowledgement));
        Assert.Null(O2Permit.Create(O2Result.Contract, Id, O2Permit.Confirmation, ""));
        var world = new World(); Assert.Equal(Outcome.NotRun, Option2Engine.Run(null, world, world).Result); Assert.Empty(world.Log);
        var permit = Permit(); world.Owner = false;
        Assert.Equal(Outcome.Fail, Option2Engine.Run(permit, world, world).Result); Assert.Equal(new[] { "owner" }, world.Log);
        world.Log.Clear(); Assert.Equal(Outcome.NotRun, Option2Engine.Run(permit, world, world).Result); Assert.Empty(world.Log);
        Assert.Contains("not durable replay", O2Presentation.Preview(Id));
    }

    [Fact]
    public void SignatureMismatchIsNotPossessionAndNeverReachesExport()
    {
        var world = new World { GoodSignature = false }; var result = Run(world);
        Assert.Equal(Outcome.Fail, result.Result); Assert.DoesNotContain("size", world.Log);
        Assert.Equal(1, world.DeleteCount); Assert.Equal("NO-GO", result.PolicyProof);
    }

    [Fact]
    public void UnexpectedAllocatorCapacityClearsAllBytesWithoutFill()
    {
        var world = new World { SizeCode = null }; byte[]? output = null;
        var result = Run(world, n => output = Enumerable.Repeat((byte)77, n + 1).ToArray());
        Assert.Equal(Outcome.Fail, result.Result); Assert.All(output!, b => Assert.Equal(0, b));
        Assert.DoesNotContain("fill", world.Log); Assert.Equal(1, world.DeleteCount);
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(5)] [InlineData(6)] [InlineData(8)]
    public void UnsupportedRequiredGetterStaysInconclusiveAndNeverExports(int property)
    {
        var world = new World { UnsupportedProperty = (MetadataProperty)property };
        var result = Run(world);
        Assert.Equal(Outcome.Inconclusive, result.Result);
        Assert.Contains(result.Evidence, e => e.Phase == O2Phase.Metadata && e.Property == (MetadataProperty)property && e.HResult == unchecked((int)0x80090029));
        Assert.DoesNotContain("size", world.Log); Assert.Equal(1, world.DeleteCount);
        Assert.Equal("NO-GO", result.PolicyProof);
    }

    [Fact]
    public void OwnershipProtocolAndAclEvidenceCannotBeRelaxedToPass()
    {
        var acl = new O2AclEvidence(true, true, 1, true, true, true);
        Assert.True(Option2JournalSecurity.Restricted(acl, true));
        foreach (var bad in new[] { acl with { OwnerMatches = false }, acl with { Protected = false },
            acl with { RuleCount = 2 }, acl with { OnlyOwnerPrincipal = false }, acl with { AllowRule = false }, acl with { FullControl = false } })
            Assert.False(Option2JournalSecurity.Restricted(bad, true));
        Assert.True(Option2JournalSecurity.Restricted(acl with { Protected = false }, false)); // inherited owner-only file rule, protected parent
        Assert.False(Option2JournalProtocol.Allowed(null, O2Stage.Bound, []));
        Assert.False(Option2JournalProtocol.Allowed(O2Stage.Created, O2Stage.Bound, new byte[91]));
        Assert.False(Option2JournalProtocol.Allowed(O2Stage.Planned, O2Stage.DeleteAttempted, []));
        Assert.False(Option2JournalProtocol.Allowed(O2Stage.Completed, O2Stage.DeleteAttempted, []));
        Assert.False(Option2JournalProtocol.Allowed(O2Stage.DeleteAttempted, O2Stage.DeleteAttempted, []));
        Assert.False(Option2JournalProtocol.Allowed(O2Stage.Completed, O2Stage.CleanupUnconfirmed, []));
        Assert.True(Option2JournalProtocol.Allowed(O2Stage.Bound, O2Stage.DeleteAttempted, []));
    }

    [Theory]
    [InlineData("provider", true)] [InlineData("provider", false)]
    [InlineData("create", true)] [InlineData("create", false)]
    [InlineData("reopen", true)] [InlineData("reopen", false)]
    public void PartialAcquisitionReleaseNeverBecomesPersistentKeyCleanup(string phase, bool released)
    {
        var world = new World { PartialAt = phase, FailedRelease = released ? null : phase == "provider" ? "provider" : phase == "create" ? "original" : "reopened" };
        var result = Run(world);
        Assert.Equal(0, world.DeleteCount);
        Assert.NotEqual(O2Cleanup.ExactDeleteAndJournalConfirmed, result.Cleanup);
        Assert.Equal(phase == "provider" && released ? Outcome.Inconclusive : Outcome.Fail, result.Result);
        Assert.Contains(result.Evidence, e => e.Category == "PartialAcquisitionNotPersistentKeyCleanup");
        Assert.True(world.Original.FreeCount <= 1); Assert.True(world.Reopened.FreeCount <= 1); Assert.Equal(1, world.Provider.FreeCount);
    }

    [Fact]
    public void TruncatedPersistedUiPolicyCannotPassOrReachPrivateExport()
    {
        var world = new World { MalformedUi = true }; var result = Run(world);
        Assert.Equal(Outcome.Fail, result.Result); Assert.DoesNotContain("size", world.Log);
        Assert.Contains(result.Evidence, e => e.Property == MetadataProperty.KeyUiPolicy && e.Phase == O2Phase.Metadata);
        Assert.Equal(1, world.DeleteCount);
    }

    [Fact]
    public void MissingKeyPreflightCannotTreatGenericErrorsAsAbsence()
    {
        Assert.True(O2Plan.DefinitiveMissing(unchecked((int)0x80090016), true));
        Assert.False(O2Plan.DefinitiveMissing(unchecked((int)0x80090016), false));
        foreach (int code in new[] { 0, unchecked((int)0x80090029), unchecked((int)0x80090010), unchecked((int)0x80090011) })
            Assert.False(O2Plan.DefinitiveMissing(code, true));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void PrivateOutputIsClearedBeforeAnyExportOutcomeJournalFlush(bool rejection)
    {
        var world = new World { SizeCode = null, FillCode = rejection ? unchecked((int)0x80090029) : null };
        byte[]? output = null; int checkedRecords = 0;
        world.BeforeJournal = stage => {
            if (stage is O2Stage.ExportRejected or O2Stage.UnexpectedOutput) {
                Assert.All(output!, b => Assert.Equal(0, b)); checkedRecords++;
            }
        };
        Assert.Equal(Outcome.Fail, Run(world, n => output = new byte[n]).Result);
        Assert.Equal(rejection ? 2 : 1, checkedRecords);
        Assert.Equal(1, world.DeleteCount);
    }

    private sealed class Key(World world, string kind) : IO2Key
    {
        internal int CloseCount, FreeCount;
        public O2HandleState State { get; internal set; } = O2HandleState.Owned;
        public bool? ReleaseSucceeded { get; private set; }
        public int? ReleaseHResult => ReleaseSucceeded is null ? null : ReleaseSucceeded.Value ? 0 : unchecked((int)0x80090026);
        public void Dispose()
        {
            CloseCount++;
            if (State == O2HandleState.ConsumedByDelete) throw new Exception("must not free consumed handle");
            if (State == O2HandleState.OwnershipUnknown) { ReleaseSucceeded = false; return; }
            if (State != O2HandleState.Owned) throw new Exception("double close");
            FreeCount++; world.Step("close-" + kind); State = O2HandleState.Released; ReleaseSucceeded = world.FailedRelease != kind;
        }
    }
    private sealed class Resource(World world) : IResource
    {
        internal int FreeCount;
        public bool? ReleaseSucceeded { get; private set; }
        public void Dispose() { FreeCount++; world.Step("close-provider"); ReleaseSucceeded = world.FailedRelease != "provider"; }
    }
    private sealed class Journal(World world) : IO2Journal
    {
        internal O2Stage? Stage;
        internal int CloseCount, VerifyCount;
        internal bool Broken;
        private byte[]? binding;
        public bool? ReleaseSucceeded { get; private set; }
        public void Append(O2Stage stage, byte[]? publicBinding = null)
        {
            world.BeforeJournal?.Invoke(stage);
            world.Step("journal-" + stage);
            if (Broken || !Option2JournalProtocol.Allowed(Stage, stage, publicBinding)) throw new InvalidOperationException();
            if (world.InterruptedStage == stage && !world.InterruptAfterFlush) { Broken = true; throw new Exception("flush failure"); }
            Stage = stage; if (stage == O2Stage.Bound) binding = publicBinding!.ToArray();
            if (world.InterruptedStage == stage) { Broken = true; throw new Exception("interruption after flush"); }
        }
        public bool Verify(O2Plan plan, ReadOnlySpan<byte> selected)
        {
            VerifyCount++; world.Step("verify-journal");
            return !Broken && world.InvalidJournalVerify != VerifyCount && plan.Operation == Id && binding is not null && binding.AsSpan().SequenceEqual(selected);
        }
        public void Dispose() { CloseCount++; world.Step("close-journal"); ReleaseSucceeded = world.FailedRelease != "journal"; }
    }
    private sealed class World : IO2Calls, IO2Journals
    {
        internal readonly List<string> Log = [];
        internal readonly List<(MetadataProperty Property, uint Value, int Flags)> Sets = [];
        internal readonly Resource Provider;
        internal readonly Key Original, Reopened;
        internal readonly Journal Journal;
        internal (uint Version, uint Flags, int CallFlags) UiSet;
        internal string? ProviderName, Algorithm, CreatedName, ThrowAt, FailedRelease, PartialAt;
        internal int CreateLegacy, CreateFlags, FinalizeFlags, DeleteCount, BadBindingRead, SpkiReads, WrongMetadata = -1, Size = 104, Filled = 104, InvalidJournalVerify;
        internal int? SizeCode = unchecked((int)0x80090029), FillCode;
        internal bool CreateSideEffectFirst;
        internal bool Owner = true, ExistingKey, JournalCreationFailure, PersistentKey, MalformedUi, GoodSignature = true, WritePartial = true, InterruptAfterFlush;
        internal O2Stage? InterruptedStage;
        internal int DeleteMode;
        internal MetadataProperty? UnsupportedProperty;
        internal Action? BeforeDelete;
        internal Action<O2Stage>? BeforeJournal;
        private byte[]? signedHash;
        private static byte[] Binding() { var value = new byte[91]; O2Binding.Prefix.CopyTo(value, 0); value.AsSpan(27).Fill(0x55); return value; }
        internal World() { Provider = new(this); Original = new(this, "original"); Reopened = new(this, "reopened"); Journal = new(this); }
        internal void Step(string name) { Log.Add(name); if (ThrowAt == name) throw new Exception("SECRET path SID key bytes"); }
        public bool OrdinaryOwner() { Step("owner"); return Owner; }
        public IResource OpenProvider(string name, int flags) { ProviderName = name; Assert.Equal(0, flags); Step("provider"); if (PartialAt == "provider") { Provider.Dispose(); throw new AcquisitionFailure(unchecked((int)0x80090029), Phase.ReleaseProvider, Provider.ReleaseSucceeded == true, null); } return Provider; }
        public bool ExactKeyExists(IResource provider, string name, int flags) { Assert.Same(Provider, provider); Assert.Equal(new O2Plan(Id).KeyName, name); Assert.Equal(0, flags); Step("preflight"); return ExistingKey; }
        public IO2Journal CreateFresh(O2Plan plan) { Step("journal-create"); if (JournalCreationFailure) throw new InvalidOperationException(); Assert.Equal(Id, plan.Operation); return Journal; }
        public IO2Key Create(IResource provider, string algorithm, string name, int legacy, int flags)
        { Assert.Same(Provider, provider); Algorithm = algorithm; CreatedName = name; CreateLegacy = legacy; CreateFlags = flags; if (CreateSideEffectFirst) PersistentKey = true; Step("create"); PersistentKey = true; if (PartialAt == "create") { Original.Dispose(); throw new AcquisitionFailure(unchecked((int)0x80090029), Phase.ReleaseKey, Original.ReleaseSucceeded == true, Original.ReleaseHResult); } return Original; }
        public void SetDword(IO2Key key, MetadataProperty property, uint value, int flags) { Assert.Same(Original, key); Sets.Add((property, value, flags)); Step("set-" + property); }
        public void SetUiPolicy(IO2Key key, uint version, uint flags, int callFlags) { Assert.Same(Original, key); UiSet = (version, flags, callFlags); Step("ui-set"); }
        public void FinalizeKey(IO2Key key, int flags) { Assert.Same(Original, key); FinalizeFlags = flags; Step("finalize"); }
        public IO2Key Reopen(IResource provider, string name, int legacy, int flags)
        { Assert.Equal(new O2Plan(Id).KeyName, name); Assert.Equal(0, legacy); Assert.Equal(0, flags); Assert.Equal(O2HandleState.Released, Original.State); Step("reopen"); if (PartialAt == "reopen") { Reopened.Dispose(); throw new AcquisitionFailure(unchecked((int)0x80090029), Phase.ReleaseKey, Reopened.ReleaseSucceeded == true, Reopened.ReleaseHResult); } return Reopened; }
        public string ReadText(IResource resource, MetadataProperty property)
        {
            Step("get-" + property);
            if (UnsupportedProperty == property) throw new MetadataReadFailure(property, MetadataReadError.NativeStatus, unchecked((int)0x80090029));
            return property switch {
                MetadataProperty.ProviderName => O2Plan.Provider, MetadataProperty.KeyName => new O2Plan(Id).KeyName,
                MetadataProperty.KeyAlgorithmName => WrongMetadata == 0 ? "wrong" : O2Plan.Algorithm,
                MetadataProperty.KeyAlgorithmGroup => WrongMetadata == 1 ? "wrong" : "ECDSA", _ => throw new InvalidOperationException()
            };
        }
        public uint ReadDword(IO2Key key, MetadataProperty property)
        {
            Step("get-" + property);
            if (UnsupportedProperty == property) throw new MetadataReadFailure(property, MetadataReadError.NativeStatus, unchecked((int)0x80090029));
            return property switch { MetadataProperty.KeyType => 0u, MetadataProperty.KeyLength => WrongMetadata == 2 ? 384u : 256u,
                MetadataProperty.KeyExportPolicy => WrongMetadata == 3 ? 3u : 0u, MetadataProperty.KeyUsage => WrongMetadata == 4 ? 7u : 2u, _ => throw new InvalidOperationException() };
        }
        public byte[] ReadUiPolicy(IO2Key key) { Step("ui-get"); var bytes = new byte[MalformedUi ? 8 : InteropContracts.UiPolicySize(IntPtr.Size)]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, 1); if (WrongMetadata == 5) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 1); return bytes; }
        public byte[] PublicSpki(IO2Key key) { Step("public-spki"); SpkiReads++; var value = Binding(); if (BadBindingRead == SpkiReads) value[^1] ^= 1; return value; }
        public byte[] FreshChallenge() { Step("challenge"); return Enumerable.Repeat((byte)0x33, 32).ToArray(); }
        public byte[] SignHash(IO2Key key, ReadOnlySpan<byte> hash, int flags) { Assert.Same(Reopened, key); Assert.Equal(32, hash.Length); Assert.Equal(0, flags); Step("sign"); signedHash = hash.ToArray(); return new byte[64]; }
        public bool VerifyHash(ReadOnlySpan<byte> binding, ReadOnlySpan<byte> hash, ReadOnlySpan<byte> signature)
        { Step("verify-signature"); Assert.True(binding.SequenceEqual(Binding())); Assert.True(hash.SequenceEqual(signedHash)); Assert.Equal(64, signature.Length); return GoodSignature; }
        private static void ExportArgs(string blob, IntPtr wrap, IntPtr parameters, int flags) { Assert.Equal("ECCPRIVATEBLOB", blob); Assert.Equal(IntPtr.Zero, wrap); Assert.Equal(IntPtr.Zero, parameters); Assert.Equal(0, flags); }
        public int QueryPrivate(IO2Key key, string blob, IntPtr wrap, IntPtr parameters, int flags) { ExportArgs(blob, wrap, parameters, flags); Step("size"); if (SizeCode is int code) throw new NativeFailure(code); return Size; }
        public int FillPrivate(IO2Key key, string blob, IntPtr wrap, IntPtr parameters, Span<byte> output, int flags)
        { ExportArgs(blob, wrap, parameters, flags); Assert.Equal(104, output.Length); if (WritePartial) output.Fill(0x55); Step("fill"); if (FillCode is int code) throw new NativeFailure(code); BinaryPrimitives.WriteUInt32LittleEndian(output, 0x32534345); BinaryPrimitives.WriteUInt32LittleEndian(output[4..], 32); return Filled; }
        public O2Deletion Delete(IO2Key key, int flags)
        {
            Assert.Equal(0, flags); Assert.Equal(O2HandleState.Owned, key.State); DeleteCount++; Assert.Equal(1, DeleteCount); BeforeDelete?.Invoke(); Step("delete");
            var target = (Key)key;
            if (DeleteMode == 1) return new(unchecked((int)0x80090010), target.State);
            if (DeleteMode == 2) { target.State = O2HandleState.OwnershipUnknown; return new(null, target.State); }
            if (DeleteMode == 3) return new(0, target.State); // invalid success without ownership transition
            target.State = O2HandleState.ConsumedByDelete; PersistentKey = false;
            return new(DeleteMode == 4 ? unchecked((int)0x80090010) : 0, target.State);
        }
    }
}
