using System.Buffers.Binary;
using System.Security.Cryptography;

namespace PeripheralBatteryCompanion.EphemeralCapability;

// Native implementation is NEVER linked into portable tests. Fakes implement only these bounded interfaces.
internal static class CapabilityEngine
{
    internal const int BlobSize = 8 + 3 * 32;
    internal static bool ValidStructure(ReadOnlySpan<byte> blob) => blob.Length == BlobSize &&
        BinaryPrimitives.ReadUInt32LittleEndian(blob) == 0x32534345 && // BCRYPT_ECDSA_PRIVATE_P256_MAGIC (ECS2)
        BinaryPrimitives.ReadUInt32LittleEndian(blob[4..]) == 32;

    internal static MetadataVerdict Evaluate(KeyMetadata value) => value switch {
        { SoftwareProvider: false } => MetadataVerdict.ProviderMismatch,
        { EcdsaP256: false } => MetadataVerdict.AlgorithmMismatch,
        { Bits: not 256 } => MetadataVerdict.BitLengthMismatch,
        { ExportPolicy: not 3 } => MetadataVerdict.ExportPolicyMismatch,
        { CurrentUser: false } => MetadataVerdict.ScopeMismatch,
        { UnnamedReadBack: false } => MetadataVerdict.PersistenceForbidden,
        { NoUiPolicy: false } => MetadataVerdict.UiRequired,
        { SigningOnly: false } => MetadataVerdict.UsageMismatch,
        _ => MetadataVerdict.Expected
    };

    internal static CapabilityResult Run(string? operation, string? confirmation, ICapabilityApi api,
        Func<int, byte[]>? allocate = null)
    {
        var evidence = new List<Evidence>();
        Outcome outcome = Outcome.NotRun;
        var memory = MemoryOutcome.NotAllocated;
        Phase phase = Phase.Consent;
        IProvider? provider = null;
        IKey? key = null;
        byte[]? output = null;
        void Pass() => evidence.Add(new(phase, Outcome.Pass));
        void Check(bool valid) { if (!valid) throw new InvalidOperationException(); Pass(); }
        if (!Consent.Allowed(operation, confirmation)) {
            evidence.Add(new(phase, Outcome.NotRun));
            return new(Outcome.NotRun, memory, evidence);
        }
        Pass();
        try {
            phase = Phase.OwnerContext; Check(api.IsOrdinaryUnelevatedUser());
            phase = Phase.OpenProvider; provider = api.OpenSoftwareProvider(); Pass();
            phase = Phase.CreateUnnamedKey; key = provider.CreateUnnamedP256(); Pass();
            phase = Phase.Configure; key.ConfigureExportableSigningOnly(); Pass();
            phase = Phase.Finalize; key.FinalizeOnce(); Pass();
            phase = Phase.Metadata;
            var actual = key.ReadMetadata();
            var metadata = Evaluate(actual);
            if (metadata != MetadataVerdict.Expected) throw new MetadataFailure(metadata);
            evidence.Add(new(phase, Outcome.Pass, Category: "ExpectedEphemeralSoftwareP256Policy3"));
            phase = Phase.Sizing;
            Check(key.QueryEccPrivateSize() == BlobSize); // No allocation for negative, oversize or unsupported lengths.
            phase = Phase.Fill;
            output = (allocate ?? (n => new byte[n]))(BlobSize);
            memory = MemoryOutcome.Unconfirmed;
            if (output.Length != BlobSize) throw new InvalidOperationException();
            Check(key.FillEccPrivate(output) == BlobSize);
            phase = Phase.Structure; Check(ValidStructure(output));
            outcome = Outcome.Pass;
        }
        catch (Exception error) {
            int? code = error is NativeFailure native ? native.Code : error is AcquisitionFailure partial ? partial.Code : null;
            outcome = code is unchecked((int)0x80090029) or unchecked((int)0x80090022) ||
                error is MetadataFailure { Verdict: MetadataVerdict.UiRequired } ? Outcome.Inconclusive : Outcome.Fail;
            evidence.Add(new(phase, outcome, code, code is not null ? "NativeStatus" :
                error is MetadataFailure invalid ? invalid.Verdict.ToString() :
                error is InvalidOperationException ? "ValidationFailure" : "UnexpectedException"));
            if (error is AcquisitionFailure acquired) {
                evidence.Add(new(acquired.ReleasePhase, acquired.Released ? Outcome.Pass : Outcome.Fail,
                    acquired.ReleaseCode, acquired.Released ? "PartialAcquisitionReleased" : "PartialAcquisitionReleaseUnconfirmed"));
                if (!acquired.Released) outcome = Outcome.Fail;
            }
        }
        finally {
            if (output is not null) {
                // Entire capacity, including partial/invalid output; no resize, serialization or copy.
                CryptographicOperations.ZeroMemory(output);
                bool cleared = true;
                foreach (byte value in output) cleared &= value == 0;
                memory = cleared ? MemoryOutcome.ClearedFullCapacity : MemoryOutcome.Unconfirmed;
                evidence.Add(new(Phase.Zeroization, cleared ? Outcome.Pass : Outcome.Fail));
                if (!cleared) outcome = Outcome.Fail;
            }
            void Close(IResource? resource, Phase closePhase)
            {
                if (resource is null) return;
                try {
                    resource.Dispose();
                    bool released = resource.ReleaseSucceeded == true;
                    evidence.Add(new(closePhase, released ? Outcome.Pass : Outcome.Fail, resource.ReleaseHResult,
                        released ? "None" : "NativeReleaseUnconfirmed"));
                    if (!released) outcome = Outcome.Fail;
                } catch {
                    outcome = Outcome.Fail;
                    evidence.Add(new(closePhase, Outcome.Fail, Category: "DisposalUnconfirmed"));
                }
            }
            Close(key, Phase.ReleaseKey);
            Close(provider, Phase.ReleaseProvider);
        }
        return new(outcome, memory, evidence);
    }
}
