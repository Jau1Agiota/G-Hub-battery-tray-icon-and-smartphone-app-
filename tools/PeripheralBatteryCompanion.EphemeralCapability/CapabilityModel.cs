namespace PeripheralBatteryCompanion.EphemeralCapability;

internal enum Outcome { NotRun, Pass, Fail, Inconclusive }
internal enum Phase { Consent, OwnerContext, OpenProvider, CreateUnnamedKey, Configure, Finalize, Metadata, Sizing, Fill, Structure, Zeroization, ReleaseKey, ReleaseProvider }
internal enum MemoryOutcome { NotAllocated, ClearedFullCapacity, Unconfirmed }
internal sealed record Evidence(Phase Phase, Outcome Outcome, int? HResult = null, string Category = "None", MetadataProperty? Property = null);
internal sealed record CapabilityResult(Outcome Capability, MemoryOutcome Memory, IReadOnlyList<Evidence> Evidence)
{
    internal const string Stop = "NATIVE CAPABILITY EXPERIMENT NOT RUN; OWNER AUTHORIZATION REQUIRED. POLICY PROOF REMAINS NO-GO.";
    internal string PolicyProof => "NO-GO";
}
internal sealed record KeyMetadata(bool SoftwareProvider, bool EcdsaP256, int Bits, uint ExportPolicy,
    bool CurrentUser, bool UnnamedReadBack, bool NoUiPolicy, bool SigningOnly);
internal enum MetadataVerdict { Expected, ProviderMismatch, AlgorithmMismatch, BitLengthMismatch, ExportPolicyMismatch,
    ScopeMismatch, PersistenceForbidden, UiRequired, UsageMismatch }
internal sealed class MetadataFailure(MetadataVerdict verdict) : Exception { internal MetadataVerdict Verdict { get; } = verdict; }
internal interface IResource : IDisposable { bool? ReleaseSucceeded { get; } int? ReleaseHResult => null; }
internal interface ICapabilityApi
{
    bool IsOrdinaryUnelevatedUser();
    IProvider OpenSoftwareProvider();
}
internal interface IProvider : IResource { IKey CreateUnnamedP256(); }
internal interface IKey : IResource
{
    void ConfigureExportableSigningOnly();
    void FinalizeOnce();
    KeyMetadata ReadMetadata();
    int QueryEccPrivateSize();
    int FillEccPrivate(Span<byte> output);
}
internal sealed class NativeFailure(int code) : Exception
{
    internal int Code { get; } = code;
}
internal sealed class AcquisitionFailure(int code, Phase releasePhase, bool released, int? releaseCode) : Exception
{
    internal int Code { get; } = code;
    internal Phase ReleasePhase { get; } = releasePhase;
    internal bool Released { get; } = released;
    internal int? ReleaseCode { get; } = releaseCode;
}
internal static class Consent
{
    internal const string Confirmation = "APPROVE-EPHEMERAL-CAPABILITY-ONLY";
    internal static bool ValidId(string? id) => id is not null && id.Length == 32 &&
        !id.Equals("3c4a7c190ef34f2e90625276aa9cbfdb", StringComparison.OrdinalIgnoreCase) &&
        !id.Equals("3bb5de0629d640569843367dd3407fc8", StringComparison.OrdinalIgnoreCase) &&
        Guid.TryParseExact(id, "N", out var value) && value != Guid.Empty && id[12] == '4' && "89abAB".Contains(id[16]);
    internal static bool Allowed(string? id, string? confirmation) => ValidId(id) && confirmation == Confirmation;
    internal static string Preview(string id)
    {
        if (!ValidId(id)) throw new ArgumentException("Invalid operation.");
        return $"PREVIEW ONLY; operation={id}. Future scope: one unnamed ephemeral CurrentUser Microsoft Software KSP ECDSA P-256 key; export policy=3 (AllowExport|AllowPlaintextExport); signing only; one ECCPRIVATEBLOB sizing call and one fill, flags=0; full 104-byte caller buffer cleared; owned handles disposed. No persisted key/name, certificate, journal/directory, DPAPI, network or production resource. No fallback/retry. Flags=0 cannot guarantee suppression of provider UI; unexpected UI requires STOP without interaction. No execution command supplied. {CapabilityResult.Stop}";
    }
}
