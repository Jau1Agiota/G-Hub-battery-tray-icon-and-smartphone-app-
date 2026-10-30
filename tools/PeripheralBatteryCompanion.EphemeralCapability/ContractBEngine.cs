using System.Security.Cryptography;

namespace PeripheralBatteryCompanion.EphemeralCapability;

// Separate from strict Contract A. No failure path calls A or manufactures its absent metadata fields.
internal static class ContractBEngine
{
    internal static string? InvalidMetadata(BMetadata value) => value switch {
        { SoftwareProvider: false } => "ProviderMismatch",
        { EcdsaP256Name: false } => "AlgorithmNameMismatch",
        { EcdsaGroup: false } => "AlgorithmGroupMismatch",
        { Bits: not 256 } => "BitLengthMismatch",
        { KeyType: not 0 } => "ScopeMismatch",
        { Usage: not 2 } => "UsageMismatch",
        _ => null
    };
    internal static BResult Run(BPermit? permit, IBApi api, Func<int, byte[]>? allocate = null)
    {
        var evidence = new List<BEvidence>();
        if (permit is null || !permit.TryConsume()) return new(Outcome.NotRun, MemoryOutcome.NotAllocated, [new(BPhase.Consent, Outcome.NotRun)]);
        evidence.Add(new(BPhase.Consent, Outcome.Pass, Category: "B1WithExplicitUiRiskAcknowledgement"));
        IBProvider? provider = null;
        IBKey? key = null;
        byte[]? output = null;
        var memory = MemoryOutcome.NotAllocated;
        var outcome = Outcome.NotRun;
        var phase = BPhase.OwnerContext;
        void Pass(string category = "None") => evidence.Add(new(phase, Outcome.Pass, Category: category));
        void Check(bool valid, string category = "None") { if (!valid) throw new InvalidOperationException(); Pass(category); }
        try {
            Check(api.IsOrdinaryUnelevatedUser());
            phase = BPhase.OpenProvider; provider = api.OpenSoftwareProvider(); Pass();
            phase = BPhase.CreateUnnamedKey; key = provider.CreateUnnamedP256(); Pass();
            phase = BPhase.Construction; Check(key.Construction.IsExpected, "NullNameUserFlags0ConstructionOnlyNotDiskProof");
            phase = BPhase.SetExportPolicy; key.SetExportPolicy(); Pass("Requested3SetReturnedSuccessNotReadback");
            phase = BPhase.SetSigningUsage; key.SetSigningUsage(); Pass("SigningOnly2SetReturnedSuccess");
            phase = BPhase.Finalize; key.FinalizeOnce(); Pass();
            phase = BPhase.Metadata;
            var invalid = InvalidMetadata(key.ReadMetadata());
            if (invalid is not null) {
                evidence.Add(new(phase, Outcome.Fail, Category: invalid));
                throw new InvalidOperationException();
            }
            Pass("RetainedReadbacksOnlyNoUiNameOrPolicyAssessment");
            phase = BPhase.Sizing; Check(key.QueryExportSize() == CapabilityEngine.BlobSize);
            phase = BPhase.Fill;
            output = (allocate ?? (n => new byte[n]))(CapabilityEngine.BlobSize);
            memory = MemoryOutcome.Unconfirmed;
            if (output.Length != CapabilityEngine.BlobSize) throw new InvalidOperationException();
            Check(key.FillExport(output) == CapabilityEngine.BlobSize);
            phase = BPhase.Structure; Check(CapabilityEngine.ValidStructure(output), "EphemeralExportFrameObservedNotPolicyEnforcement");
            outcome = Outcome.Pass;
        } catch (Exception error) {
            int? code = error is NativeFailure native ? native.Code : error is AcquisitionFailure partial ? partial.Code :
                error is MetadataReadFailure read ? read.Code : null;
            outcome = code is unchecked((int)0x80090029) or unchecked((int)0x80090022) ? Outcome.Inconclusive : Outcome.Fail;
            if (!evidence.Any(e => e.Phase == phase && e.Outcome == Outcome.Fail))
                evidence.Add(new(phase, outcome, code, code is not null ? "NativeStatus" : error is MetadataReadFailure badRead
                    ? badRead.Error == MetadataReadError.MalformedValue ? "MalformedMetadata" : "UnexpectedMetadataException"
                    : error is InvalidOperationException ? "ValidationFailure" : "UnexpectedException",
                    error is MetadataReadFailure propertyRead ? propertyRead.Property : null));
            if (error is AcquisitionFailure acquired) {
                evidence.Add(new(acquired.ReleasePhase == Phase.ReleaseKey ? BPhase.ReleaseKey : BPhase.ReleaseProvider,
                    acquired.Released ? Outcome.Pass : Outcome.Fail, acquired.ReleaseCode,
                    acquired.Released ? "PartialAcquisitionReleased" : "PartialAcquisitionReleaseUnconfirmed"));
                if (!acquired.Released) outcome = Outcome.Fail;
            }
        } finally {
            try {
                if (output is not null) {
                    CryptographicOperations.ZeroMemory(output);
                    bool cleared = true; foreach (byte b in output) cleared &= b == 0;
                    memory = cleared ? MemoryOutcome.ClearedFullCapacity : MemoryOutcome.Unconfirmed;
                    evidence.Add(new(BPhase.Zeroization, cleared ? Outcome.Pass : Outcome.Fail));
                    if (!cleared) outcome = Outcome.Fail;
                }
            } finally {
                void Close(IResource? resource, BPhase closePhase)
                {
                    if (resource is null) return;
                    try {
                        resource.Dispose();
                        bool released = resource.ReleaseSucceeded == true;
                        evidence.Add(new(closePhase, released ? Outcome.Pass : Outcome.Fail, resource.ReleaseHResult,
                            released ? "None" : "ReleaseUnconfirmed"));
                        if (!released) outcome = Outcome.Fail;
                    } catch {
                        outcome = Outcome.Fail;
                        evidence.Add(new(closePhase, Outcome.Fail, Category: "DisposalUnconfirmed"));
                    }
                }
                try { Close(key, BPhase.ReleaseKey); } finally { Close(provider, BPhase.ReleaseProvider); }
            }
        }
        return new(outcome, memory, evidence);
    }
}
