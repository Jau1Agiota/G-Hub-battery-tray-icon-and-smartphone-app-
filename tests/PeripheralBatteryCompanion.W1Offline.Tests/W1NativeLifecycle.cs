using System;
using System.Formats.Asn1;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace PeripheralBatteryCompanion.W1Offline.Tests;

// SOURCE ONLY / NOT BUILD-VERIFIED. No caller can obtain a GateEPermit in this
// revision. This type has no native constructor/static initialization or default
// provider instance. Gate I does not authorize invoking the code below.
internal static class W1NativeLifecycle
{
    internal static W1Evidence Execute(W1PreflightPlan? plan, GateEPermit? permit, DateTimeOffset now)
    {
        var evidence = new W1Evidence();
        if (!W1PreflightPlan.TryConsume(plan, permit, now)) return evidence;
        // Environment.Version is DEFENSE IN DEPTH ONLY. Matching major/minor/build
        // permits any servicing revision; it proves neither exact loaded CoreCLR
        // nor System.Security.Cryptography, selected host, assembly binary hashes,
        // SDK, token, KSP or provider. Host/binary provenance remains Gate B/E work.
        Version runtime = Environment.Version;
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitOperatingSystem ||
            !Environment.Is64BitProcess || RuntimeInformation.ProcessArchitecture != Architecture.X64 ||
            !Environment.UserInteractive || runtime.Major != 10 || runtime.Minor != 0 || runtime.Build != 13)
            return evidence;
        if (plan is null) return evidence;
        RunAuthorizedBody(plan, evidence);
        return evidence;
    }

    private static void RunAuthorizedBody(W1PreflightPlan plan, W1Evidence evidence)
    {
        CngKey? original = null;
        ECDsaCng? signer = null;
        X509Certificate2? leaf = null;
        try
        {
            evidence.Phase = W1Phase.CreateKey;
            // Set before the factory: failure may have acquired native resources
            // internally even when no owned reference returns to this method.
            evidence.NativeEntered = true;
            original = CngKey.Create(CngAlgorithm.ECDsaP256, keyName: null,
                new CngKeyCreationParameters
                {
                    Provider = CngProvider.MicrosoftSoftwareKeyStorageProvider,
                    KeyCreationOptions = CngKeyCreationOptions.None,
                    ExportPolicy = CngExportPolicies.None,
                    KeyUsage = CngKeyUsages.Signing,
                    ParentWindowHandle = IntPtr.Zero,
                    UIPolicy = null
                });

            evidence.Phase = W1Phase.Metadata;
            // Explicit allowlist; no getters that turn absence into zero and no
            // HasProperty+fetch race, retries, provider handles, ACL or key scans.
            Observe(original, MetadataField.ExportPolicy, "Export Policy", CngPropertyOptions.None, plan, evidence);
            Observe(original, MetadataField.Usage, "Key Usage", CngPropertyOptions.None, plan, evidence);
            Observe(original, MetadataField.Scope, "Key Type", CngPropertyOptions.None, plan, evidence);
            Observe(original, MetadataField.Bits, "PublicKeyLength", CngPropertyOptions.None, plan, evidence);
            Observe(original, MetadataField.Algorithm, "Algorithm Name", CngPropertyOptions.None, plan, evidence);
            Observe(original, MetadataField.Group, "Algorithm Group", CngPropertyOptions.None, plan, evidence);
            Observe(original, MetadataField.ClrMarker, "CLR IsEphemeral", CngPropertyOptions.CustomProperty, plan, evidence);
            Observe(original, MetadataField.Name, "Name", CngPropertyOptions.None, plan, evidence);

            evidence.Phase = W1Phase.WrapSigner;
            signer = new ECDsaCng(original); // existing-key reference only, no key factory
            var subject = new X500DistinguishedName(plan.SyntheticSubject!);
            var request = new CertificateRequest(subject, signer, HashAlgorithmName.SHA256);
            Require(W1PreflightPlan.TryTestIp(plan.CanonicalTestIp, out var ip));
            var san = new SubjectAlternativeNameBuilder();
            san.AddIpAddress(ip!);
            request.CertificateExtensions.Add(san.Build(plan.SanCritical!.Value));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, plan.EkuCritical!.Value));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature, plan.UsageCritical!.Value));
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(
                certificateAuthority: false, hasPathLengthConstraint: false, pathLengthConstraint: 0,
                critical: plan.BasicCritical!.Value));
            Require(W1PreflightPlan.TrySerial(plan.PublicSerialHex, out var serial));
            evidence.Phase = W1Phase.CreateLeaf;
            var generator = X509SignatureGenerator.CreateForECDsa(signer);
            leaf = request.Create(subject, generator, plan.NotBeforeUtc!.Value, plan.NotAfterUtc!.Value, serial!);

            evidence.Phase = W1Phase.InspectLeaf;
            Require(!leaf.HasPrivateKey);
            Require(leaf.SignatureAlgorithm.Value == "1.2.840.10045.4.3.2");
            Require(leaf.SubjectName.RawData.AsSpan().SequenceEqual(subject.RawData));
            Require(leaf.IssuerName.RawData.AsSpan().SequenceEqual(subject.RawData));
            Require(leaf.NotBefore.ToUniversalTime() == plan.NotBeforeUtc!.Value.UtcDateTime);
            Require(leaf.NotAfter.ToUniversalTime() == plan.NotAfterUtc!.Value.UtcDateTime);
            byte[] observedSerial = leaf.GetSerialNumber();
            Array.Reverse(observedSerial);
            Require(observedSerial.AsSpan().SequenceEqual(serial));

            // PublicKey DER export avoids GetECDsaPublicKey's extra import/wrapper.
            byte[] leafSpki = leaf.PublicKey.ExportSubjectPublicKeyInfo();
            byte[] signerSpki = generator.PublicKey.ExportSubjectPublicKeyInfo();
            byte[] publicDer = leaf.RawData;
            long acceptedBytes = (long)leafSpki.Length + signerSpki.Length + publicDer.Length + observedSerial.Length + 64;
            Require(acceptedBytes <= plan.PublicBufferAcceptanceBytes!.Value);
            // POST-call accepted-output bound only; framework/native temporaries
            // and SHA256 backend allocations are not hard bounded by this sum.
            Require(leafSpki.AsSpan().SequenceEqual(signerSpki));
            byte[] leafPin = SHA256.HashData(leafSpki);
            byte[] signerPin = SHA256.HashData(signerSpki);
            Require(leafPin.AsSpan().SequenceEqual(signerPin));
            InspectExtensions(leaf, ip!.GetAddressBytes(), plan);
            evidence.PublicLeafObserved = true;
            evidence.Outcome = "LIMITED_OBSERVATIONS_CLEANUP_UNCONFIRMED";
        }
        catch (ObservationStop)
        {
            evidence.RecordFailure(evidence.Phase, "STOP_INCONCLUSIVE");
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        {
            // Redact the ordinary exceptions handled here. Catastrophic failures
            // and abrupt process death have no reporting/cleanup guarantee.
            evidence.RecordFailure(evidence.Phase, "STOP_INCONCLUSIVE", error.HResult);
        }
        finally
        {
            // Each returned owner attempted once. No aliases independently freed,
            // key deletion/reopen/enumeration, journal or cleanup-success claim.
            try { DisposeOnce(leaf, W1Phase.DisposeLeaf, evidence); }
            finally
            {
                try { DisposeOnce(signer, W1Phase.DisposeSigner, evidence); }
                finally { DisposeOnce(original, W1Phase.DisposeKey, evidence); }
            }
        }

        // Gate E must approve the exact new local path and output/privacy rules.
        // No report is written at all on the default NOT_RUN/preflight path.
        evidence.Phase = W1Phase.Report;
        FileStream? destination = null;
        bool flushed = false;
        bool closed = false;
        try
        {
            // Snapshot before file I/O: the file cannot attest its own successful
            // write/flush/disposal. Later report errors remain in returned evidence.
            byte[] report = evidence.Render(plan.ReportAcceptanceBytes!.Value);
            destination = new FileStream(plan.NewReportDestination!, FileMode.CreateNew,
                FileAccess.Write, FileShare.None);
            destination.Write(report);
            destination.Flush(flushToDisk: true);
            flushed = true;
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        {
            evidence.RecordFailure(W1Phase.Report, "STOP_REPORT_WRITE_UNCONFIRMED", error.HResult);
            // Preserve any partial file; do not delete, overwrite or retry.
        }
        finally
        {
            // An explicit disposal attempt preserves a write/flush failure even
            // when closing also fails; no using-generated exception can mask it.
            if (destination is not null)
            {
                try { destination.Dispose(); closed = true; }
                catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
                {
                    evidence.RecordFailure(W1Phase.DisposeReport, "STOP_REPORT_WRITE_UNCONFIRMED", error.HResult);
                }
            }
        }
        evidence.ReportWriteReturned = flushed && closed;
    }

    private static void Observe(CngKey key, MetadataField field, string property, CngPropertyOptions options,
        W1PreflightPlan plan, W1Evidence evidence)
    {
        MetadataStatus status;
        int? errorCode = null;
        try
        {
            // GetProperty allocates provider-reported size internally and GetValue
            // clones. This limit is deliberately described as POST-call acceptance.
            byte[]? bytes = key.GetProperty(property, options).GetValue();
            status = W1MetadataClassifier.Classify(field, bytes, absent: false, plan.MetadataAcceptanceBytes!.Value);
        }
        catch (CryptographicException error)
        {
            errorCode = error.HResult;
            status = error.HResult == unchecked((int)0x80090011)
                ? MetadataStatus.Absent : MetadataStatus.ProviderError;
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        {
            errorCode = error.HResult;
            status = MetadataStatus.Error;
        }
        evidence.Metadata.Add(new MetadataObservation(field, status, errorCode));
        if (!W1MetadataClassifier.MayContinue(field, status)) throw new ObservationStop();
    }

    private static void InspectExtensions(X509Certificate2 leaf, byte[] expectedIp, W1PreflightPlan plan)
    {
        Require(leaf.Extensions.Count == 4);
        Require(leaf.Extensions.Cast<X509Extension>().Select(x => x.Oid?.Value).Distinct().Count() == 4);
        foreach (X509Extension extension in leaf.Extensions)
        {
            var reader = new AsnReader(extension.RawData, AsnEncodingRules.DER);
            switch (extension.Oid?.Value)
            {
                case "2.5.29.17":
                    Require(extension.Critical == plan.SanCritical!.Value);
                    var names = reader.ReadSequence();
                    byte[] actualIp = names.ReadOctetString(new Asn1Tag(TagClass.ContextSpecific, 7));
                    Require(actualIp.AsSpan().SequenceEqual(expectedIp));
                    names.ThrowIfNotEmpty();
                    break;
                case "2.5.29.37":
                    Require(extension.Critical == plan.EkuCritical!.Value);
                    var usages = reader.ReadSequence();
                    Require(usages.ReadObjectIdentifier() == "1.3.6.1.5.5.7.3.1");
                    usages.ThrowIfNotEmpty();
                    break;
                case "2.5.29.15":
                    Require(extension.Critical == plan.UsageCritical!.Value);
                    byte[] bits = reader.ReadBitString(out int unused);
                    Require(unused == 7 && bits.Length == 1 && bits[0] == 0x80);
                    break;
                case "2.5.29.19":
                    Require(extension.Critical == plan.BasicCritical!.Value);
                    // DER DEFAULT false must be absent; no path length/extra fields.
                    reader.ReadSequence().ThrowIfNotEmpty();
                    break;
                default: throw new ObservationStop();
            }
            reader.ThrowIfNotEmpty();
        }
    }

    private static void DisposeOnce(IDisposable? owner, W1Phase phase, W1Evidence evidence)
    {
        if (owner is null)
        {
            evidence.Cleanup.Add(new CleanupObservation(phase, false, false));
            return;
        }
        bool returned = false;
        try { owner.Dispose(); returned = true; }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        {
            evidence.RecordFailure(phase, "STOP_CLEANUP_UNCONFIRMED", error.HResult);
        }
        evidence.Cleanup.Add(new CleanupObservation(phase, true, returned));
    }

    private static void Require(bool observed)
    {
        if (!observed) throw new ObservationStop();
    }

    private sealed class ObservationStop : Exception { }
}
