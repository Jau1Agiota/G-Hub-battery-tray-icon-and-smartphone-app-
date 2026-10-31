namespace PeripheralBatteryCompanion.EphemeralCapability;

internal enum BPhase { Consent, OwnerContext, OpenProvider, CreateUnnamedKey, Construction, SetExportPolicy,
    SetSigningUsage, Finalize, Metadata, Sizing, Fill, Structure, Zeroization, ReleaseKey, ReleaseProvider }
internal enum BAssessment { NotAssessedByContract }
internal sealed record BEvidence(BPhase Phase, Outcome Outcome, int? HResult = null, string Category = "None", MetadataProperty? Property = null);
internal sealed record BConstruction(string Provider, int ProviderFlags, string Algorithm, string? KeyName, int LegacySpec, int KeyFlags)
{
    internal bool IsExpected => Provider == WindowsProvider && ProviderFlags == 0 && Algorithm == "ECDSA_P256" &&
        KeyName is null && LegacySpec == 0 && KeyFlags == 0;
    internal const string WindowsProvider = "Microsoft Software Key Storage Provider";
}
internal sealed record BMetadata(bool SoftwareProvider, bool EcdsaP256Name, bool EcdsaGroup, int Bits, uint KeyType, uint Usage);
internal sealed record BResult(Outcome Capability, MemoryOutcome Memory, IReadOnlyList<BEvidence> Evidence)
{
    internal const string Contract = "EphemeralCapability/B1";
    internal uint RequestedExportPolicy => 3;
    internal uint? ActualExportPolicy => null;
    internal BAssessment ExportPolicyReadback => BAssessment.NotAssessedByContract;
    internal BAssessment UiPolicySetAndReadback => BAssessment.NotAssessedByContract;
    internal BAssessment KeyNameReadback => BAssessment.NotAssessedByContract;
    internal string PolicyProof => "NO-GO";
    internal Outcome ExportPolicySet => Evidence.FirstOrDefault(e => e.Phase == BPhase.SetExportPolicy)?.Outcome ?? Outcome.NotRun;
}
internal interface IBApi { bool IsOrdinaryUnelevatedUser(); IBProvider OpenSoftwareProvider(); }
internal interface IBProvider : IResource { IBKey CreateUnnamedP256(); }
internal interface IBKey : IResource
{
    BConstruction Construction { get; }
    void SetExportPolicy();
    void SetSigningUsage();
    void FinalizeOnce();
    BMetadata ReadMetadata();
    int QueryExportSize();
    int FillExport(Span<byte> output);
}
internal sealed class BPermit
{
    private int consumed;
    internal string Operation { get; }
    private BPermit(string operation) { Operation = operation; }
    internal const string Confirmation = "APPROVE-B1-CAPABILITY-ONLY";
    internal const string UiAcknowledgement = "ACKNOWLEDGE-FLAGS0-UI-RISK";
    internal static BPermit? Create(string? contract, string? operation, string? confirmation, string? uiAcknowledgement) =>
        contract == BResult.Contract && Consent.ValidId(operation) && confirmation == Confirmation && uiAcknowledgement == UiAcknowledgement
            ? new(operation!) : null;
    internal static BPermit? FromArguments(string[] args) => args.Length == 5 && args[0] == "--reviewed-contract-b"
        ? Create(args[1], args[2], args[3], args[4]) : null;
    internal bool TryConsume() => Interlocked.CompareExchange(ref consumed, 1, 0) == 0;
}
internal static class BPresentation
{
    internal const string Stop = "NATIVE EXPERIMENT NOT RUN; OWNER AUTHORIZATION REQUIRED FOR ANY FUTURE RUN; POLICY PROOF REMAINS NO-GO.";
    internal static string Preview(string operation)
    {
        if (!Consent.ValidId(operation)) throw new ArgumentException("Invalid operation.");
        return $"PREVIEW ONLY; contract={BResult.Contract}; operation={operation}. One unnamed CurrentUser software-KSP P256 key, mandatory export-policy SET3/signing SET2, flags0. UI Policy SET/GET, key Name GET and export-policy GET: NOT ASSESSED BY CONTRACT. NULL-name construction is not disk-absence proof. Provider UI possible; supervised owner only; STOP without interaction on unexpected UI. No technical no-dialog/automatic-cancellation guarantee. One ECCPRIVATEBLOB size/fill,104-byte output, full owned-buffer clearing and owned releases only. No persisted key/certificate/journal/DPAPI/network or target. No fallback/retry; no execution command. {Stop}";
    }
    internal static IEnumerable<string> Format(BResult result)
    {
        yield return $"contract={BResult.Contract}; capability={result.Capability}; ephemeral capability only";
        yield return $"requested-export-policy={result.RequestedExportPolicy}; SET={result.ExportPolicySet}; export-policy-readback=NOT ASSESSED BY CONTRACT; actual-policy-value=NOT ASSESSED";
        yield return "UI-Policy-SET/GET=NOT ASSESSED BY CONTRACT; key-Name-GET=NOT ASSESSED BY CONTRACT; no NoUiPolicy=true claim";
        foreach (BPhase phase in Enum.GetValues<BPhase>()) {
            var evidence = result.Evidence.Where(e => e.Phase == phase).ToArray();
            if (evidence.Length == 0) yield return $"phase={phase}; status=NotRun";
            foreach (var item in evidence)
                yield return $"phase={phase}; status={item.Outcome}; category={item.Category}; property={MetadataProperties.Label(item.Property)}; HRESULT={(item.HResult is int code ? $"0x{code:X8}" : "none")}";
        }
        yield return $"zeroization={result.Memory}; provider-UI-possible=true; no technical no-dialog guarantee; unexpected UI invalidates run; POLICY PROOF={result.PolicyProof}; STOP for owner review";
    }
}
