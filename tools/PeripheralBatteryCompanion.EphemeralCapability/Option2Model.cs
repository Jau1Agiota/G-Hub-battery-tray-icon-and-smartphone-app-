namespace PeripheralBatteryCompanion.EphemeralCapability;

internal enum O2Phase { Consent, Owner, Provider, Preflight, Journal, Create, Configure, SetExportPolicy, SetSigningUsage, SetUiPolicy, Finalize, Binding,
    CloseCreated, Reopen, Identity, Metadata, Possession, JournalGate, Sizing, Fill, Zeroization,
    CleanupValidate, Delete, CleanupRecord, ReleaseCreated, ReleaseReopened, ReleaseProvider, JournalClose }
internal enum O2Stage { Planned, Created, Finalized, Bound, Reopened, Validated, PossessionVerified,
    ExportAttempted, ExportRejected, UnexpectedOutput, DeleteAttempted, Deleted, CleanupUnconfirmed, Completed }
internal enum O2HandleState { Owned, Released, ConsumedByDelete, OwnershipUnknown }
internal enum O2Cleanup { NoKeyCreationAttempted, OrphanUnconfirmed, Unconfirmed, ExactDeleteAndJournalConfirmed }
internal sealed record O2Evidence(O2Phase Phase, Outcome Outcome, string Category = "None", int? HResult = null, MetadataProperty? Property = null);
internal sealed record O2Result(Outcome Result, MemoryOutcome Memory, O2Cleanup Cleanup, IReadOnlyList<O2Evidence> Evidence, O2OwnershipState Ownership = O2OwnershipState.NotStarted)
{
    internal const string Contract = "PersistedKeyEvidence/Option2-v1";
    internal string PolicyProof => "NO-GO";
    internal uint RequestedPolicy => 0;
}
internal sealed record O2Plan(string Operation)
{
    internal string KeyName => "PBC-A22C6-Option2-" + Operation;
    internal const string Provider = "Microsoft Software Key Storage Provider";
    internal const string Algorithm = "ECDSA_P256";
    // Match Microsoft's CngKey.Exists missing-key status; other errors are NOT absence.
    internal static bool DefinitiveMissing(int status, bool invalidOutput) => status == unchecked((int)0x80090016) && invalidOutput;
}
internal sealed class O2Permit
{
    private int used;
    internal O2Plan Plan { get; }
    private O2Permit(string id) { Plan = new(id); }
    internal const string Confirmation = "APPROVE-ISOLATED-PERSISTED-OPTION2";
    internal const string UiAcknowledgement = "ACKNOWLEDGE-FLAGS0-UI-AND-ORPHAN-RISK";
    internal static bool ValidId(string? id) => Consent.ValidId(id) &&
        !string.Equals(id, "6df3c74dc1804991a04730bd39aa519d", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(id, "ff28360e68f249439294475e8da21614", StringComparison.OrdinalIgnoreCase); // preview-only; never execution approval
    internal static O2Permit? Create(string? contract, string? id, string? confirmation, string? ui) =>
        contract == O2Result.Contract && ValidId(id) && confirmation == Confirmation && ui == UiAcknowledgement ? new(id!) : null;
    internal static O2Permit? Parse(string[] args) => args.Length == 5 && args[0] == "--reviewed-option2"
        ? Create(args[1], args[2], args[3], args[4]) : null;
    internal bool Consume() => Interlocked.CompareExchange(ref used, 1, 0) == 0;
}
internal interface IO2Key : IResource { O2HandleState State { get; } }
internal sealed record O2Deletion(int? Code, O2HandleState State);
internal interface IO2Calls
{
    bool OrdinaryOwner();
    IResource OpenProvider(string name, int flags);
    bool ExactKeyExists(IResource provider, string name, int flags);
    IO2Key Create(IResource provider, string algorithm, string name, int legacy, int flags);
    void SetDword(IO2Key key, MetadataProperty property, uint value, int flags);
    void SetUiPolicy(IO2Key key, uint version, uint flags, int callFlags);
    void FinalizeKey(IO2Key key, int flags);
    IO2Key Reopen(IResource provider, string name, int legacy, int flags);
    string ReadText(IResource resource, MetadataProperty property);
    uint ReadDword(IO2Key key, MetadataProperty property);
    byte[] ReadUiPolicy(IO2Key key);
    byte[] PublicSpki(IO2Key key);
    byte[] FreshChallenge();
    byte[] SignHash(IO2Key key, ReadOnlySpan<byte> hash, int flags);
    bool VerifyHash(ReadOnlySpan<byte> spki, ReadOnlySpan<byte> hash, ReadOnlySpan<byte> signature);
    int QueryPrivate(IO2Key key, string blob, IntPtr wrap, IntPtr parameters, int flags);
    int FillPrivate(IO2Key key, string blob, IntPtr wrap, IntPtr parameters, Span<byte> output, int flags);
    O2Deletion Delete(IO2Key key, int flags);
}
internal interface IO2Journal : IResource
{
    void Append(O2Stage stage, byte[]? publicBinding = null);
    bool Verify(O2Plan plan, ReadOnlySpan<byte> binding);
}
internal interface IO2Journals { IO2Journal CreateFresh(O2Plan plan); }
internal static class O2Binding
{
    // Fixed canonical public P256 SPKI. Copies are PUBLIC ONLY; no private coordinate/scalar buffer enters this type.
    internal static readonly byte[] Prefix = Convert.FromHexString("3059301306072A8648CE3D020106082A8648CE3D03010703420004");
    internal static bool Valid(ReadOnlySpan<byte> spki) => spki.Length == 91 && spki[..27].SequenceEqual(Prefix);
}
internal static class O2Presentation
{
    internal const string Stop = "NATIVE NOT RUN; OWNER APPROVAL REQUIRED; POLICY PROOF NO-GO.";
    internal static string Preview(string id)
    {
        if (!O2Permit.ValidId(id)) throw new ArgumentException("Invalid operation.");
        return $"PREVIEW ONLY; contract={O2Result.Contract}; operation={id}; exactly one fresh CurrentUser Microsoft Software KSP ECDSA_P256 named key PBC-A22C6-Option2-<operation>, policy SET0/readback0, signing2, flags0; one isolated owner-restricted local operation directory/ownership journal retained. Persisted-key UI Policy version1/flags0 SET/GET; no guarantee of no dialog/cancellation. Local possession challenge; at most one ECCPRIVATEBLOB size/fill,104-byte caller output cleared; exact owned-key DeleteKey consumes handle on success. No cert/store/PFX/DPAPI/network, paired control or production identity. Ordinary SET/finalize/public-binding failure BEFORE durable binding can leave an orphan named key even after handle release; no automatic deletion without binding. Failure/crash may leave orphan; never adopt/retry/repair/cleanup old IDs. One-use permit is not durable replay protection. No execution command. {Stop}";
    }
    internal static IEnumerable<string> Format(O2Result result)
    {
        yield return $"contract={O2Result.Contract}; result={result.Result}; requested-policy=0; POLICY PROOF={result.PolicyProof}";
        foreach (var phase in Enum.GetValues<O2Phase>()) {
            var entries = result.Evidence.Where(e => e.Phase == phase).ToArray();
            if (entries.Length == 0) yield return $"phase={phase}; status=NotRun";
            foreach (var entry in entries) yield return $"phase={phase}; status={entry.Outcome}; category={entry.Category}; property={MetadataProperties.Label(entry.Property)}; HRESULT={(entry.HResult is int code ? $"0x{code:X8}" : "none")}";
        }
        yield return $"memory={result.Memory}; cleanup={result.Cleanup}; ownership-proof-state={result.Ownership}; rejection is not policy causality; provider UI possible; STOP for owner review; production/policy=NO-GO";
    }
}
