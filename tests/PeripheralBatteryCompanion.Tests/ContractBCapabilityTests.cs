using System.Buffers.Binary;
using PeripheralBatteryCompanion.EphemeralCapability;
using Xunit;

namespace PeripheralBatteryCompanion.Tests;

// Production B call-flow with a pure native-call recorder. No Windows adapter, Main, CNG or actual key material.
public sealed class ContractBCapabilityTests
{
    private const string Id = "1234567812344234a2341234567890ab";
    private static BPermit Permit() => BPermit.Create(BResult.Contract, Id, BPermit.Confirmation, BPermit.UiAcknowledgement)!;
    private static BResult Run(Recorder calls, Func<int, byte[]>? allocate = null) => ContractBEngine.Run(Permit(), new ContractBNativeFlow(calls), allocate);

    [Fact]
    public void ExactBCallPathHasMandatorySetsAndRetainedReadsButNoneOfFourRemovedCalls()
    {
        var calls = new Recorder(); byte[]? output = null;
        var result = Run(calls, n => output = new byte[n]);
        Assert.Equal(Outcome.Pass, result.Capability);
        Assert.Equal(new[] { "owner", "open", "create", "set-KeyExportPolicy", "set-KeyUsage", "finalize", "get-ProviderName",
            "get-KeyAlgorithmName", "get-KeyAlgorithmGroup", "get-KeyLength", "get-KeyType", "get-KeyUsage", "size", "fill", "close-key", "close-provider" }, calls.Log);
        Assert.Equal(BConstruction.WindowsProvider, calls.OpenName);
        Assert.Equal(0, calls.OpenFlags);
        Assert.Equal("ECDSA_P256", calls.Algorithm);
        Assert.Null(calls.KeyName);
        Assert.Equal(0, calls.Legacy);
        Assert.Equal(0, calls.KeyFlags);
        Assert.Equal(new[] { (MetadataProperty.KeyExportPolicy, 3u, 0), (MetadataProperty.KeyUsage, 2u, 0) }, calls.Sets);
        Assert.Equal(0, calls.FinalizeFlags);
        Assert.All(calls.Exports, e => { Assert.Equal(IntPtr.Zero, e.Wrap); Assert.Equal(IntPtr.Zero, e.Parameters); Assert.Equal("ECCPRIVATEBLOB", e.Blob); Assert.Equal(0, e.Flags); });
        Assert.Equal(104, calls.Capacity);
        Assert.All(output!, b => Assert.Equal(0, b));
        Assert.Equal(MemoryOutcome.ClearedFullCapacity, result.Memory);
        Assert.Equal(1, calls.Key.CloseCount); Assert.Equal(1, calls.Provider.CloseCount);
        Assert.True(result.Evidence.ToList().FindIndex(e => e.Phase == BPhase.Zeroization) < result.Evidence.ToList().FindIndex(e => e.Phase == BPhase.ReleaseKey));
        Assert.Null(result.ActualExportPolicy);
        Assert.Equal(3u, result.RequestedExportPolicy);
        Assert.Equal(Outcome.Pass, result.ExportPolicySet);
        Assert.Equal(BAssessment.NotAssessedByContract, result.UiPolicySetAndReadback);
        Assert.Equal(BAssessment.NotAssessedByContract, result.KeyNameReadback);
        Assert.Equal(BAssessment.NotAssessedByContract, result.ExportPolicyReadback);
        Assert.Equal("NO-GO", result.PolicyProof);
        string text = string.Join("\n", BPresentation.Format(result));
        Assert.Contains("NOT ASSESSED BY CONTRACT", text);
        Assert.Contains("actual-policy-value=NOT ASSESSED", text);
        Assert.Contains("provider-UI-possible=true", text);
        Assert.DoesNotContain("actual-policy-value=3", text);
        Assert.DoesNotContain("NoUiPolicy=Pass", text);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(8)]
    public void RequiredGetterUnsupportedStopsBeforeExportAndCleans(int property)
    {
        var calls = new Recorder { BadRead = (MetadataProperty)property };
        var result = Run(calls);
        Assert.Equal(Outcome.Inconclusive, result.Capability);
        Assert.Equal(MemoryOutcome.NotAllocated, result.Memory);
        Assert.DoesNotContain("size", calls.Log); Assert.DoesNotContain("fill", calls.Log);
        Assert.Contains(result.Evidence, e => e.Phase == BPhase.Metadata && e.Property == (MetadataProperty)property && e.HResult == unchecked((int)0x80090029));
        Assert.Equal(1, calls.Key.CloseCount); Assert.Equal(1, calls.Provider.CloseCount);
        Assert.Equal("NO-GO", result.PolicyProof);
    }

    [Theory]
    [InlineData("set-KeyExportPolicy")]
    [InlineData("set-KeyUsage")]
    public void UnsupportedMandatorySetIsNotSilentlyOmitted(string setting)
    {
        var calls = new Recorder { ErrorAt = setting, NativeCode = unchecked((int)0x80090029) };
        var result = Run(calls);
        Assert.Equal(Outcome.Inconclusive, result.Capability);
        Assert.DoesNotContain("finalize", calls.Log); Assert.DoesNotContain("size", calls.Log);
        Assert.Equal(setting == "set-KeyExportPolicy" ? Outcome.Inconclusive : Outcome.Pass, result.ExportPolicySet);
        Assert.Null(result.ActualExportPolicy);
        Assert.Equal(1, calls.Key.CloseCount); Assert.Equal(1, calls.Provider.CloseCount);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public void WrongRetainedMetadataNeverReachesSizing(int field)
    {
        var calls = new Recorder { WrongMetadata = field };
        Assert.Equal(Outcome.Fail, Run(calls).Capability);
        Assert.DoesNotContain("size", calls.Log);
        Assert.Equal(1, calls.Key.CloseCount); Assert.Equal(1, calls.Provider.CloseCount);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void UnsupportedSizeOrPartialFillCannotPassAndNeverRetries(bool fill)
    {
        var calls = new Recorder { ErrorAt = fill ? "fill" : "size", NativeCode = unchecked((int)0x80090029) }; byte[]? output = null;
        var result = Run(calls, n => output = new byte[n]);
        Assert.Equal(Outcome.Inconclusive, result.Capability);
        Assert.Equal(fill ? MemoryOutcome.ClearedFullCapacity : MemoryOutcome.NotAllocated, result.Memory);
        if (fill) Assert.All(output!, b => Assert.Equal(0, b)); else Assert.Null(output);
        Assert.Equal(1, calls.Log.Count(s => s == "size"));
        Assert.Equal(fill ? 1 : 0, calls.Log.Count(s => s == "fill"));
    }

    [Theory]
    [InlineData(-1)] [InlineData(0)] [InlineData(103)] [InlineData(105)] [InlineData(int.MaxValue)]
    public void WrongSizingCannotAllocateOrExport(int size)
    {
        var calls = new Recorder { Size = size };
        var result = Run(calls, _ => throw new Exception("must not allocate"));
        Assert.Equal(Outcome.Fail, result.Capability);
        Assert.Equal(MemoryOutcome.NotAllocated, result.Memory);
        Assert.DoesNotContain("fill", calls.Log);
    }

    [Theory]
    [InlineData(-1)] [InlineData(0)] [InlineData(103)] [InlineData(105)] [InlineData(int.MaxValue)]
    public void ChangedFillSizeClearsEntireOutput(int size)
    {
        var calls = new Recorder { FillLength = size }; byte[]? output = null;
        Assert.Equal(Outcome.Fail, Run(calls, n => output = new byte[n]).Capability);
        Assert.All(output!, b => Assert.Equal(0, b));
        Assert.Equal(1, calls.Log.Count(s => s == "fill"));
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void MalformedHeaderFailsAndClears(bool badMagic)
    {
        var calls = new Recorder { BadMagic = badMagic, BadWidth = !badMagic }; byte[]? output = null;
        Assert.Equal(Outcome.Fail, Run(calls, n => output = new byte[n]).Capability);
        Assert.All(output!, b => Assert.Equal(0, b));
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("open")]
    [InlineData("create")]
    [InlineData("set-KeyExportPolicy")]
    [InlineData("set-KeyUsage")]
    [InlineData("finalize")]
    [InlineData("get-KeyAlgorithmName")]
    [InlineData("fill")]
    public void UnexpectedExceptionsRemainRedactedAndDisposeAcquiredResources(string stage)
    {
        var calls = new Recorder { ErrorAt = stage }; byte[]? output = null;
        var result = Run(calls, n => output = new byte[n]);
        Assert.Equal(Outcome.Fail, result.Capability);
        Assert.DoesNotContain("SECRET", string.Join("\n", BPresentation.Format(result)));
        Assert.Equal(stage is "owner" or "open" or "create" ? 0 : 1, calls.Key.CloseCount);
        Assert.Equal(stage is "owner" or "open" ? 0 : 1, calls.Provider.CloseCount);
        if (output is not null) Assert.All(output, b => Assert.Equal(0, b));
    }

    [Theory]
    [InlineData("close-key", false)]
    [InlineData("close-provider", false)]
    [InlineData("close-key", true)]
    [InlineData("close-provider", true)]
    public void ReleaseFailureOrExceptionAlwaysOverridesSuccessfulExport(string resource, bool throws)
    {
        var calls = new Recorder { ErrorAt = throws ? resource : null, FailedRelease = throws ? null : resource };
        var result = Run(calls);
        Assert.Equal(Outcome.Fail, result.Capability);
        Assert.Equal(MemoryOutcome.ClearedFullCapacity, result.Memory);
        Assert.Equal(1, calls.Key.CloseCount); Assert.Equal(1, calls.Provider.CloseCount);
        Assert.Equal("NO-GO", result.PolicyProof);
    }

    [Theory]
    [InlineData(true, true)] [InlineData(true, false)] [InlineData(false, true)] [InlineData(false, false)]
    public void PartialAcquisitionEvidenceDoesNotDoubleCloseOrHideReleaseFailure(bool opening, bool releaseOk)
    {
        var calls = new Recorder { PartialAt = opening ? "open" : "create", FailedRelease = releaseOk ? null : opening ? "close-provider" : "close-key" };
        var result = Run(calls);
        Assert.Equal(releaseOk ? Outcome.Inconclusive : Outcome.Fail, result.Capability);
        Assert.Equal(opening ? 0 : 1, calls.Key.CloseCount);
        Assert.Equal(1, calls.Provider.CloseCount);
        Assert.DoesNotContain("set-KeyExportPolicy", calls.Log);
        Assert.Contains(result.Evidence, e => e.Category.StartsWith("PartialAcquisition", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(unchecked((int)0x80090022), true)]
    [InlineData(unchecked((int)0x80090010), false)]
    [InlineData(unchecked((int)0x8009000B), false)]
    public void SilentContextAndOtherRejectionsNeverPromotePolicyOrTriggerFlagsFallback(int code, bool inconclusive)
    {
        var calls = new Recorder { ErrorAt = "fill", NativeCode = code };
        var result = Run(calls);
        Assert.Equal(inconclusive ? Outcome.Inconclusive : Outcome.Fail, result.Capability);
        Assert.Equal(MemoryOutcome.ClearedFullCapacity, result.Memory);
        Assert.All(calls.Exports, e => Assert.Equal(0, e.Flags));
        Assert.Equal(1, calls.Log.Count(s => s == "fill"));
    }

    [Fact]
    public void OneUseContractBoundPermitCannotBeReplayedEvenAfterFailure()
    {
        var permit = Permit(); var calls = new Recorder { Ordinary = false };
        Assert.Equal(Outcome.Fail, ContractBEngine.Run(permit, new ContractBNativeFlow(calls)).Capability);
        calls.Log.Clear();
        Assert.Equal(Outcome.NotRun, ContractBEngine.Run(permit, new ContractBNativeFlow(calls)).Capability);
        Assert.Empty(calls.Log);
        Assert.Null(BPermit.Create("A", Id, BPermit.Confirmation, BPermit.UiAcknowledgement));
        Assert.Null(BPermit.Create(BResult.Contract, Id, Consent.Confirmation, BPermit.UiAcknowledgement));
        Assert.Null(BPermit.Create(BResult.Contract, Id, BPermit.Confirmation, ""));
        Assert.Null(BPermit.FromArguments(["--owner-approved-ephemeral-only", Id, Consent.Confirmation]));
        Assert.Null(BPermit.FromArguments(["--reviewed-contract-b", BResult.Contract, Id, BPermit.Confirmation]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("00000000000000000000000000000000")]
    [InlineData("123456781234123412341234567890ab")]
    [InlineData("3c4a7c190ef34f2e90625276aa9cbfdb")]
    [InlineData("3C4A7C190EF34F2E90625276AA9CBFDB")]
    [InlineData("3bb5de0629d640569843367dd3407fc8")]
    [InlineData("3BB5DE0629D640569843367DD3407FC8")]
    public void InvalidOrRetiredIdCannotEnterAnyNativeCall(string? id)
    {
        var calls = new Recorder();
        var permit = BPermit.Create(BResult.Contract, id, BPermit.Confirmation, BPermit.UiAcknowledgement);
        Assert.Null(permit);
        Assert.Equal(Outcome.NotRun, ContractBEngine.Run(permit, new ContractBNativeFlow(calls)).Capability);
        Assert.Empty(calls.Log);
    }

    [Fact]
    public void FixedConstructionRejectsNonNullOrChangedArgumentsWithoutInventingReadback()
    {
        var expected = new BConstruction(BConstruction.WindowsProvider, 0, "ECDSA_P256", null, 0, 0);
        Assert.True(expected.IsExpected);
        Assert.False((expected with { KeyName = "" }).IsExpected);
        Assert.False((expected with { KeyName = "SECRET user key" }).IsExpected);
        Assert.False((expected with { KeyFlags = 32 }).IsExpected);
        Assert.False((expected with { LegacySpec = 1 }).IsExpected);
        Assert.False((expected with { Algorithm = "ECDH_P256" }).IsExpected);
        Assert.False((expected with { ProviderFlags = 1 }).IsExpected);
        var preview = BPresentation.Preview(Id);
        Assert.Contains(BResult.Contract, preview);
        Assert.Contains("NOT ASSESSED BY CONTRACT", preview);
        Assert.Contains("no technical no-dialog", preview, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No fallback/retry", preview);
    }

    [Fact]
    public void ProtocolPreventsRepeatedSetFinalizeMetadataAndExportAttempts()
    {
        var calls = new Recorder(); var flow = new ContractBNativeFlow(calls);
        using var provider = flow.OpenSoftwareProvider(); using var key = provider.CreateUnnamedP256();
        key.SetExportPolicy(); Assert.Throws<InvalidOperationException>(() => key.SetExportPolicy());
        key.SetSigningUsage(); Assert.Throws<InvalidOperationException>(() => key.SetSigningUsage());
        key.FinalizeOnce(); Assert.Throws<InvalidOperationException>(() => key.FinalizeOnce());
        key.ReadMetadata(); Assert.Throws<InvalidOperationException>(() => key.ReadMetadata());
        key.QueryExportSize(); Assert.Throws<InvalidOperationException>(() => key.QueryExportSize());
        byte[] output = new byte[104];
        try { key.FillExport(output); Assert.Throws<InvalidOperationException>(() => key.FillExport(output)); }
        finally { Array.Clear(output); }
        Assert.Equal(1, calls.Log.Count(s => s == "fill"));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(5)] [InlineData(7)] [InlineData(8)]
    public void MalformedRetainedReadFailsWithPropertyAndNoAllocation(int property)
    {
        var calls = new Recorder { MalformedRead = (MetadataProperty)property };
        var result = Run(calls, _ => throw new Exception("must not allocate"));
        Assert.Equal(Outcome.Fail, result.Capability);
        Assert.Contains(result.Evidence, e => e.Property == (MetadataProperty)property && e.Category == "MalformedMetadata");
        Assert.Equal(MemoryOutcome.NotAllocated, result.Memory);
        Assert.DoesNotContain("size", calls.Log);
        Assert.Equal(1, calls.Key.CloseCount); Assert.Equal(1, calls.Provider.CloseCount);
    }

    [Fact]
    public void WrongAllocationCapacityIsFullyClearedWithoutFill()
    {
        var calls = new Recorder(); byte[]? output = null;
        var result = Run(calls, n => output = Enumerable.Repeat((byte)99, n + 1).ToArray());
        Assert.Equal(Outcome.Fail, result.Capability);
        Assert.All(output!, b => Assert.Equal(0, b));
        Assert.DoesNotContain("fill", calls.Log);
        Assert.Equal(1, calls.Key.CloseCount); Assert.Equal(1, calls.Provider.CloseCount);
    }

    [Fact]
    public void StrictAStillRejectsMissingUiNameAndActualPolicyInsteadOfUsingBDefaults()
    {
        var a = new KeyMetadata(true, true, 256, 3, true, true, true, true);
        Assert.Equal(MetadataVerdict.UiRequired, CapabilityEngine.Evaluate(a with { NoUiPolicy = false }));
        Assert.Equal(MetadataVerdict.PersistenceForbidden, CapabilityEngine.Evaluate(a with { UnnamedReadBack = false }));
        Assert.Equal(MetadataVerdict.ExportPolicyMismatch, CapabilityEngine.Evaluate(a with { ExportPolicy = 0 }));
    }

    private sealed class Resource(Recorder calls, string kind) : IResource
    {
        internal string Kind => kind;
        internal int CloseCount;
        public bool? ReleaseSucceeded { get; private set; }
        public int? ReleaseHResult => ReleaseSucceeded is null ? null : ReleaseSucceeded.Value ? 0 : unchecked((int)0x80090026);
        public void Dispose() { CloseCount++; calls.Step("close-" + kind); ReleaseSucceeded = calls.FailedRelease != "close-" + kind; }
    }
    private sealed class Recorder : IBNativeCalls
    {
        internal readonly List<string> Log = [];
        internal readonly List<(MetadataProperty Property, uint Value, int Flags)> Sets = [];
        internal readonly List<(IntPtr Wrap, string Blob, IntPtr Parameters, int Flags)> Exports = [];
        internal readonly Resource Key, Provider;
        internal string? ErrorAt, FailedRelease, PartialAt;
        internal int? NativeCode;
        internal MetadataProperty? BadRead, MalformedRead;
        internal int WrongMetadata = -1, Size = 104, FillLength = 104, Capacity, OpenFlags, Legacy, KeyFlags, FinalizeFlags;
        internal bool Ordinary = true, BadMagic, BadWidth;
        internal string? OpenName, Algorithm, KeyName;
        internal Recorder() { Key = new(this, "key"); Provider = new(this, "provider"); }
        internal void Step(string stage)
        {
            Log.Add(stage);
            if (ErrorAt != stage) return;
            if (NativeCode is int code) throw new NativeFailure(code);
            throw new Exception("SECRET native path SID private bytes");
        }
        private void Partial(string stage, Resource resource, Phase phase)
        {
            if (PartialAt != stage) return;
            resource.Dispose();
            throw new AcquisitionFailure(unchecked((int)0x80090029), phase, resource.ReleaseSucceeded == true, resource.ReleaseHResult);
        }
        public bool IsOrdinaryUnelevatedUser() { Step("owner"); return Ordinary; }
        public IResource OpenProvider(string name, int flags) { OpenName = name; OpenFlags = flags; Step("open"); Partial("open", Provider, Phase.ReleaseProvider); return Provider; }
        public IResource CreateKey(IResource provider, string algorithm, string? name, int legacySpec, int flags)
        {
            Assert.Same(Provider, provider); Algorithm = algorithm; KeyName = name; Legacy = legacySpec; KeyFlags = flags;
            Step("create"); Partial("create", Key, Phase.ReleaseKey); return Key;
        }
        public void SetDword(IResource key, MetadataProperty property, uint value, int flags)
        {
            Assert.Same(Key, key); Sets.Add((property, value, flags)); Step("set-" + property);
        }
        public void FinalizeKey(IResource key, int flags) { Assert.Same(Key, key); FinalizeFlags = flags; Step("finalize"); }
        private void Read(IResource resource, MetadataProperty property)
        {
            Assert.True(ContractBNativeFlow.RetainedRead(property));
            Assert.Same(property == MetadataProperty.ProviderName ? Provider : Key, resource);
            Step("get-" + property);
            if (MalformedRead == property) throw new MetadataReadFailure(property, MetadataReadError.MalformedValue);
            if (BadRead == property) throw new MetadataReadFailure(property, MetadataReadError.NativeStatus, unchecked((int)0x80090029));
        }
        public string ReadText(IResource resource, MetadataProperty property)
        {
            Read(resource, property);
            return property switch {
                MetadataProperty.ProviderName => WrongMetadata == 0 ? "wrong" : BConstruction.WindowsProvider,
                MetadataProperty.KeyAlgorithmName => WrongMetadata == 1 ? "wrong" : "ECDSA_P256",
                MetadataProperty.KeyAlgorithmGroup => WrongMetadata == 2 ? "wrong" : "ECDSA",
                _ => throw new Exception("removed read")
            };
        }
        public uint ReadDword(IResource key, MetadataProperty property)
        {
            Read(key, property);
            return property switch {
                MetadataProperty.KeyLength => WrongMetadata == 3 ? 384u : 256u,
                MetadataProperty.KeyType => WrongMetadata == 4 ? 32u : 0u,
                MetadataProperty.KeyUsage => WrongMetadata == 5 ? 7u : 2u,
                _ => throw new Exception("removed read")
            };
        }
        public int QueryExport(IResource key, IntPtr wrappingKey, string blob, IntPtr parameters, int flags)
        {
            Assert.Same(Key, key); Exports.Add((wrappingKey, blob, parameters, flags)); Step("size"); return Size;
        }
        public int FillExport(IResource key, IntPtr wrappingKey, string blob, IntPtr parameters, Span<byte> output, int flags)
        {
            Assert.Same(Key, key); Exports.Add((wrappingKey, blob, parameters, flags)); Capacity = output.Length;
            output.Fill(0x55); Step("fill");
            BinaryPrimitives.WriteUInt32LittleEndian(output, BadMagic ? 0u : 0x32534345u);
            BinaryPrimitives.WriteUInt32LittleEndian(output[4..], BadWidth ? 31u : 32u);
            return FillLength;
        }
    }
}
