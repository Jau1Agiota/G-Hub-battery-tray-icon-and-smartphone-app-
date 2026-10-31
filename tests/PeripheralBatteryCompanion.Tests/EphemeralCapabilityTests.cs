using System.Buffers.Binary;
using PeripheralBatteryCompanion.EphemeralCapability;
using Xunit;

namespace PeripheralBatteryCompanion.Tests;

// All resources and data below are synthetic byte patterns. NO native adapter, CNG, certificates or stores linked here.
public sealed class EphemeralCapabilityTests
{
    private const string Id = "1234567812344234a2341234567890ab";
    private static readonly KeyMetadata Expected = new(true, true, 256, 3, true, true, true, true);
    private static CapabilityResult Run(FakeApi api, Func<int, byte[]>? allocate = null) =>
        CapabilityEngine.Run(Id, Consent.Confirmation, api, allocate);

    [Fact]
    public void SuccessfulCapabilityDoesNotAuthorizePolicyProofAndClearsEntireAllocation()
    {
        var api = new FakeApi(); byte[]? buffer = null;
        var result = Run(api, n => buffer = new byte[n]);
        Assert.Equal(Outcome.Pass, result.Capability);
        Assert.Equal("NO-GO", result.PolicyProof);
        Assert.Equal(MemoryOutcome.ClearedFullCapacity, result.Memory);
        Assert.All(buffer!, b => Assert.Equal(0, b));
        Assert.Equal(new[] { "owner", "open", "create", "configure", "finalize", "metadata", "size", "fill", "key-close", "provider-close" }, api.Calls);
        Assert.Equal(1, api.Provider.Key.DisposeCount);
        Assert.Equal(1, api.Provider.DisposeCount);
        var phases = result.Evidence.Select(e => e.Phase).ToList();
        Assert.True(phases.IndexOf(Phase.Zeroization) < phases.IndexOf(Phase.ReleaseKey));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnsupportedSizingOrPartialFillStaysInconclusiveAndDisposes(bool fill)
    {
        var api = new FakeApi(); byte[]? buffer = null;
        if (fill) api.Provider.Key.FillCode = unchecked((int)0x80090029);
        else api.Provider.Key.SizeCode = unchecked((int)0x80090029);
        var result = Run(api, n => buffer = new byte[n]);
        Assert.Equal(Outcome.Inconclusive, result.Capability);
        Assert.Contains(result.Evidence, e => e.Phase == (fill ? Phase.Fill : Phase.Sizing) && e.Outcome == Outcome.Inconclusive);
        Assert.Equal(fill ? MemoryOutcome.ClearedFullCapacity : MemoryOutcome.NotAllocated, result.Memory);
        if (fill) Assert.All(buffer!, b => Assert.Equal(0, b)); else Assert.Null(buffer);
        Assert.Equal(1, api.Calls.Count(s => s == "size"));
        Assert.Equal(fill ? 1 : 0, api.Calls.Count(s => s == "fill"));
        Assert.Equal(1, api.Provider.Key.DisposeCount);
        Assert.Equal(1, api.Provider.DisposeCount);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(103)]
    [InlineData(105)]
    [InlineData(int.MaxValue)]
    public void SizingMustBeExactlyP256BeforeAnyAllocation(int length)
    {
        var api = new FakeApi(); api.Provider.Key.Size = length;
        var result = Run(api, _ => throw new Exception("allocator must not run"));
        Assert.Equal(Outcome.Fail, result.Capability);
        Assert.Equal(MemoryOutcome.NotAllocated, result.Memory);
        Assert.DoesNotContain("fill", api.Calls);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(103)]
    [InlineData(105)]
    [InlineData(int.MaxValue)]
    public void ChangedFillLengthFailsAndZeroesFullCapacity(int length)
    {
        var api = new FakeApi(); api.Provider.Key.Filled = length; byte[]? buffer = null;
        var result = Run(api, n => buffer = new byte[n]);
        Assert.Equal(Outcome.Fail, result.Capability);
        Assert.Equal(MemoryOutcome.ClearedFullCapacity, result.Memory);
        Assert.All(buffer!, b => Assert.Equal(0, b));
        Assert.Equal(1, api.Calls.Count(s => s == "fill"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InvalidMagicOrComponentLengthFailsWithoutLoggingBlob(bool magic)
    {
        var api = new FakeApi(); api.Provider.Key.BadMagic = magic; api.Provider.Key.BadLength = !magic;
        byte[]? buffer = null;
        var result = Run(api, n => buffer = new byte[n]);
        Assert.Equal(Outcome.Fail, result.Capability);
        Assert.All(buffer!, b => Assert.Equal(0, b));
        Assert.Contains(result.Evidence, e => e.Phase == Phase.Structure && e.Outcome == Outcome.Fail);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void WrongMetadataNeverReachesExportIncludingForbiddenPersistence(int field)
    {
        var api = new FakeApi();
        api.Provider.Key.Metadata = field switch {
            0 => Expected with { SoftwareProvider = false }, 1 => Expected with { EcdsaP256 = false },
            2 => Expected with { Bits = 384 }, 3 => Expected with { ExportPolicy = 7 },
            4 => Expected with { CurrentUser = false }, 5 => Expected with { UnnamedReadBack = false },
            6 => Expected with { NoUiPolicy = false }, _ => Expected with { SigningOnly = false }
        };
        var result = Run(api);
        Assert.Equal(field == 6 ? Outcome.Inconclusive : Outcome.Fail, result.Capability);
        Assert.DoesNotContain("size", api.Calls);
        Assert.DoesNotContain("fill", api.Calls);
        if (field == 5) Assert.Contains(result.Evidence, e => e.Category == "PersistenceForbidden");
        Assert.Equal(1, api.Provider.Key.DisposeCount);
        Assert.Equal(1, api.Provider.DisposeCount);
    }

    [Theory]
    [InlineData(unchecked((int)0x80090022), true)]
    [InlineData(unchecked((int)0x80090010), false)]
    [InlineData(unchecked((int)0x8009000B), false)]
    public void UiContextAndUnexpectedDenialsNeverBecomeCapabilityPass(int code, bool inconclusive)
    {
        var api = new FakeApi(); api.Provider.Key.FillCode = code;
        var result = Run(api);
        Assert.Equal(inconclusive ? Outcome.Inconclusive : Outcome.Fail, result.Capability);
        Assert.Equal(MemoryOutcome.ClearedFullCapacity, result.Memory);
        Assert.Equal(1, api.Calls.Count(s => s == "fill"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CleanupFailureCannotBeHiddenBySuccessfulExportAndDoesNotRetry(bool key)
    {
        var api = new FakeApi();
        if (key) api.Provider.Key.CloseFails = true; else api.Provider.CloseFails = true;
        var result = Run(api);
        Assert.Equal(Outcome.Fail, result.Capability);
        Assert.Equal(MemoryOutcome.ClearedFullCapacity, result.Memory);
        Assert.Equal(1, api.Provider.Key.DisposeCount);
        Assert.Equal(1, api.Provider.DisposeCount);
        Assert.Contains(result.Evidence, e => e.Phase == (key ? Phase.ReleaseKey : Phase.ReleaseProvider) &&
            e.HResult == unchecked((int)0x80090020) && e.Outcome == Outcome.Fail);
    }

    [Theory]
    [InlineData("create")]
    [InlineData("configure")]
    [InlineData("finalize")]
    [InlineData("metadata")]
    public void FailureAtEachLifecycleStageDisposesOnlyAcquiredResources(string stage)
    {
        var api = new FakeApi { ThrowAt = stage };
        var result = Run(api);
        Assert.Equal(Outcome.Fail, result.Capability);
        Assert.Equal(stage == "create" ? 0 : 1, api.Provider.Key.DisposeCount);
        Assert.Equal(1, api.Provider.DisposeCount);
        Assert.DoesNotContain("size", api.Calls);
        Assert.Equal(stage is "create" or "configure" ? 0 : 1, api.Calls.Count(s => s == "finalize"));
    }

    [Fact]
    public void PartialWriteManagedExceptionIsRedactedAndBufferCleared()
    {
        var api = new FakeApi { ThrowAt = "fill" }; byte[]? buffer = null;
        var result = Run(api, n => buffer = new byte[n]);
        Assert.Equal(Outcome.Fail, result.Capability);
        Assert.All(buffer!, b => Assert.Equal(0, b));
        Assert.DoesNotContain("SECRET", string.Join(" ", result.Evidence));
        Assert.Contains(result.Evidence, e => e.Phase == Phase.Fill && e.Category == "UnexpectedException");
    }

    [Fact]
    public void UnexpectedAllocatorCapacityIsClearedAndNeverPassedToNative()
    {
        var api = new FakeApi(); byte[]? buffer = null;
        var result = Run(api, n => { buffer = Enumerable.Repeat((byte)77, n + 1).ToArray(); return buffer; });
        Assert.Equal(Outcome.Fail, result.Capability);
        Assert.All(buffer!, b => Assert.Equal(0, b));
        Assert.DoesNotContain("fill", api.Calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("3c4a7c190ef34f2e90625276aa9cbfdb")]
    [InlineData("3C4A7C190EF34F2E90625276AA9CBFDB")]
    [InlineData("00000000000000000000000000000000")]
    [InlineData("123456781234123412341234567890ab")]
    public void InvalidOrRetiredIdsCannotOpenAnyNativeResource(string? id)
    {
        var api = new FakeApi();
        Assert.Equal(Outcome.NotRun, CapabilityEngine.Run(id, Consent.Confirmation, api).Capability);
        Assert.Empty(api.Calls);
    }

    [Fact]
    public void PreviewAndWrongConsentCannotTriggerApiAndOwnerContextIsGated()
    {
        var api = new FakeApi();
        var preview = Consent.Preview(Id);
        Assert.Contains("flags=0", preview);
        Assert.Contains(CapabilityResult.Stop, preview);
        Assert.Empty(api.Calls);
        Assert.Equal(Outcome.NotRun, CapabilityEngine.Run(Id, "wrong", api).Capability);
        Assert.Empty(api.Calls);
        api.OrdinaryUser = false;
        Assert.Equal(Outcome.Fail, Run(api).Capability);
        Assert.Equal(new[] { "owner" }, api.Calls);
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("open")]
    public void PreAcquisitionErrorsDoNotInventCleanupOrExport(string stage)
    {
        var api = new FakeApi { ThrowAt = stage };
        var result = Run(api);
        Assert.Equal(Outcome.Fail, result.Capability);
        Assert.Equal(0, api.Provider.DisposeCount);
        Assert.Equal(0, api.Provider.Key.DisposeCount);
        Assert.DoesNotContain("create", api.Calls);
        Assert.DoesNotContain(result.Evidence, e => e.Phase is Phase.ReleaseKey or Phase.ReleaseProvider);
    }

    [Theory]
    [InlineData("key-close")]
    [InlineData("provider-close")]
    public void ThrowingCleanupStillAttemptsOtherOwnedResourceExactlyOnce(string stage)
    {
        var api = new FakeApi { ThrowAt = stage };
        var result = Run(api);
        Assert.Equal(Outcome.Fail, result.Capability);
        Assert.Equal(1, api.Calls.Count(s => s == "key-close"));
        Assert.Equal(1, api.Calls.Count(s => s == "provider-close"));
        Assert.Equal(MemoryOutcome.ClearedFullCapacity, result.Memory);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void PartialNativeAcquisitionReportsReleaseAndNeverDisposesTwice(bool opening, bool releaseOk)
    {
        var api = new FakeApi { PartialOpen = opening, PartialCreate = !opening };
        api.Provider.CloseFails = opening && !releaseOk;
        api.Provider.Key.CloseFails = !opening && !releaseOk;
        var result = Run(api);
        Assert.Equal(releaseOk ? Outcome.Inconclusive : Outcome.Fail, result.Capability);
        Assert.Equal(opening ? 0 : 1, api.Provider.Key.DisposeCount);
        Assert.Equal(1, api.Provider.DisposeCount);
        Assert.DoesNotContain("configure", api.Calls);
        Assert.Contains(result.Evidence, e => e.Phase == (opening ? Phase.ReleaseProvider : Phase.ReleaseKey) &&
            e.Outcome == (releaseOk ? Outcome.Pass : Outcome.Fail) &&
            e.Category.StartsWith("PartialAcquisition", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void UnsupportedMetadataIdentifiesPropertyAndDoesNotExportOrInventZeroization(int property)
    {
        var api = new FakeApi();
        api.Provider.Key.MetadataError = new MetadataReadFailure((MetadataProperty)property,
            MetadataReadError.NativeStatus, unchecked((int)0x80090029));
        var result = Run(api);
        Assert.Equal(Outcome.Inconclusive, result.Capability);
        Assert.Equal(MemoryOutcome.NotAllocated, result.Memory);
        Assert.Equal("NO-GO", result.PolicyProof);
        Assert.Contains(result.Evidence, e => e.Phase == Phase.Metadata && e.Property == (MetadataProperty)property &&
            e.Category == "NativeStatus" && e.HResult == unchecked((int)0x80090029));
        Assert.DoesNotContain("size", api.Calls);
        Assert.DoesNotContain("fill", api.Calls);
        Assert.Equal(1, api.Provider.Key.DisposeCount);
        Assert.Equal(1, api.Provider.DisposeCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MalformedOrUnexpectedMetadataFailsWithSafePropertyAndCleans(bool unexpected)
    {
        var api = new FakeApi();
        api.Provider.Key.MetadataError = new MetadataReadFailure(MetadataProperty.KeyName,
            unexpected ? MetadataReadError.UnexpectedException : MetadataReadError.MalformedValue);
        var result = Run(api);
        Assert.Equal(Outcome.Fail, result.Capability);
        Assert.Contains(result.Evidence, e => e.Property == MetadataProperty.KeyName && e.HResult is null &&
            e.Category == (unexpected ? "UnexpectedMetadataException" : "MalformedMetadata"));
        Assert.DoesNotContain("size", api.Calls);
        Assert.Equal(1, api.Provider.Key.DisposeCount);
        Assert.Equal(1, api.Provider.DisposeCount);
    }

    [Fact]
    public void SecondRetiredOperationCannotEvenReadOwnerContext()
    {
        var api = new FakeApi();
        foreach (var id in new[] { "3bb5de0629d640569843367dd3407fc8", "3BB5DE0629D640569843367DD3407FC8" })
            Assert.Equal(Outcome.NotRun, CapabilityEngine.Run(id, Consent.Confirmation, api).Capability);
        Assert.Empty(api.Calls);
    }

    private sealed class FakeApi : ICapabilityApi
    {
        internal readonly List<string> Calls = [];
        internal readonly FakeProvider Provider;
        internal string? ThrowAt;
        internal bool OrdinaryUser = true, PartialOpen, PartialCreate;
        internal FakeApi() { Provider = new(this); }
        internal void Step(string phase) { Calls.Add(phase); if (ThrowAt == phase) throw new Exception("SECRET path SID raw bytes"); }
        public bool IsOrdinaryUnelevatedUser() { Step("owner"); return OrdinaryUser; }
        public IProvider OpenSoftwareProvider() {
            Step("open");
            if (PartialOpen) {
                Provider.Dispose();
                throw new AcquisitionFailure(unchecked((int)0x80090029), Phase.ReleaseProvider, Provider.ReleaseSucceeded == true, Provider.ReleaseHResult);
            }
            return Provider;
        }
    }
    private sealed class FakeProvider(FakeApi api) : IProvider
    {
        internal FakeKey Key { get; } = new(api);
        internal int DisposeCount;
        internal bool CloseFails;
        public bool? ReleaseSucceeded { get; private set; }
        public int? ReleaseHResult => ReleaseSucceeded is null ? null : ReleaseSucceeded.Value ? 0 : unchecked((int)0x80090020);
        public IKey CreateUnnamedP256() {
            api.Step("create");
            if (api.PartialCreate) {
                Key.Dispose();
                throw new AcquisitionFailure(unchecked((int)0x80090029), Phase.ReleaseKey, Key.ReleaseSucceeded == true, Key.ReleaseHResult);
            }
            return Key;
        }
        public void Dispose() { api.Step("provider-close"); DisposeCount++; ReleaseSucceeded = !CloseFails; }
    }
    private sealed class FakeKey(FakeApi api) : IKey
    {
        internal int Size = 104, Filled = 104;
        internal int? SizeCode, FillCode;
        internal bool BadMagic, BadLength, CloseFails;
        internal int DisposeCount;
        internal KeyMetadata Metadata = Expected;
        internal MetadataReadFailure? MetadataError;
        public bool? ReleaseSucceeded { get; private set; }
        public int? ReleaseHResult => ReleaseSucceeded is null ? null : ReleaseSucceeded.Value ? 0 : unchecked((int)0x80090020);
        public void ConfigureExportableSigningOnly() => api.Step("configure");
        public void FinalizeOnce() => api.Step("finalize");
        public KeyMetadata ReadMetadata() { api.Step("metadata"); if (MetadataError is not null) throw MetadataError; return Metadata; }
        public int QueryEccPrivateSize() { api.Step("size"); if (SizeCode is int code) throw new NativeFailure(code); return Size; }
        public int FillEccPrivate(Span<byte> output)
        {
            output.Fill(0x55); // synthetic partial/private-looking bytes; not an actual key
            api.Step("fill");
            if (FillCode is int code) throw new NativeFailure(code);
            BinaryPrimitives.WriteUInt32LittleEndian(output, BadMagic ? 0u : 0x32534345u);
            BinaryPrimitives.WriteUInt32LittleEndian(output[4..], BadLength ? 31u : 32u);
            return Filled;
        }
        public void Dispose() { api.Step("key-close"); DisposeCount++; ReleaseSucceeded = !CloseFails; }
    }
}
