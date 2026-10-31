using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PeripheralBatteryCompanion.W1Offline.Tests;

internal enum MetadataField { ExportPolicy, Usage, Scope, Bits, Algorithm, Group, ClrMarker, Name }
internal enum MetadataStatus { Absent, Malformed, ProviderError, Error, Mismatch, ObservedExpectedMetadata, EmptyName }
internal enum W1Phase { Preflight, CreateKey, Metadata, WrapSigner, CreateLeaf, InspectLeaf, DisposeLeaf, DisposeSigner, DisposeKey, Report, DisposeReport }
internal sealed record MetadataObservation(MetadataField Field, MetadataStatus Status, int? HResult = null);
internal sealed record CleanupObservation(W1Phase Phase, bool Attempted, bool DisposeReturned);
internal sealed record FailureObservation(W1Phase Phase, int? HResult);

// Pure classifier only. Never receives private-key bytes. Missing is not zero.
internal static class W1MetadataClassifier
{
    internal static MetadataStatus Classify(MetadataField field, byte[]? bytes, bool absent, int acceptanceLimit)
    {
        if (absent) return MetadataStatus.Absent;
        if (bytes is null || bytes.Length == 0 || bytes.Length > acceptanceLimit)
            return MetadataStatus.Malformed;
        ReadOnlySpan<byte> value = bytes;
        switch (field)
        {
            case MetadataField.ExportPolicy: return Dword(value, 0);
            case MetadataField.Usage: return Dword(value, 2);
            case MetadataField.Scope: return Dword(value, 0);
            case MetadataField.Bits: return Dword(value, 256);
            case MetadataField.ClrMarker:
                return value.Length != 1 ? MetadataStatus.Malformed :
                    value[0] == 1 ? MetadataStatus.ObservedExpectedMetadata : MetadataStatus.Mismatch;
            case MetadataField.Algorithm: return ExactString(value, "ECDSA_P256");
            case MetadataField.Group: return ExactString(value, "ECDSA");
            case MetadataField.Name:
                if (!CanonicalUtf16(value)) return MetadataStatus.Malformed;
                return value.Length == 2 ? MetadataStatus.EmptyName : MetadataStatus.Mismatch;
            default: return MetadataStatus.Error;
        }
    }

    internal static bool MayContinue(MetadataField field, MetadataStatus status) =>
        field == MetadataField.Name
            ? status is MetadataStatus.Absent or MetadataStatus.EmptyName
            : status == MetadataStatus.ObservedExpectedMetadata;

    private static MetadataStatus Dword(ReadOnlySpan<byte> value, uint expected) =>
        value.Length != 4 ? MetadataStatus.Malformed :
        BinaryPrimitives.ReadUInt32LittleEndian(value) == expected
            ? MetadataStatus.ObservedExpectedMetadata : MetadataStatus.Mismatch;

    private static MetadataStatus ExactString(ReadOnlySpan<byte> value, string expected)
    {
        if (!CanonicalUtf16(value)) return MetadataStatus.Malformed;
        if (value.Length != (expected.Length + 1) * 2) return MetadataStatus.Mismatch;
        for (int i = 0; i < expected.Length; i++)
            if (BinaryPrimitives.ReadUInt16LittleEndian(value.Slice(i * 2, 2)) != expected[i])
                return MetadataStatus.Mismatch;
        return MetadataStatus.ObservedExpectedMetadata;
    }

    private static bool CanonicalUtf16(ReadOnlySpan<byte> value)
    {
        if (value.Length < 2 || value.Length % 2 != 0 || value[^1] != 0 || value[^2] != 0)
            return false;
        for (int i = 0; i < value.Length - 2; i += 2)
        {
            ushort c = BinaryPrimitives.ReadUInt16LittleEndian(value.Slice(i, 2));
            if (c == 0) return false;
            if (c is >= 0xD800 and <= 0xDBFF)
            {
                i += 2;
                if (i >= value.Length - 2) return false;
                ushort next = BinaryPrimitives.ReadUInt16LittleEndian(value.Slice(i, 2));
                if (next is < 0xDC00 or > 0xDFFF) return false;
            }
            else if (c is >= 0xDC00 and <= 0xDFFF) return false;
        }
        return true;
    }
}

internal sealed class W1Evidence
{
    internal string Outcome { get; set; } = "NOT_RUN";
    internal bool NativeEntered { get; set; }
    internal bool PublicLeafObserved { get; set; }
    internal bool ReportWriteReturned { get; set; }
    internal W1Phase Phase { get; set; } = W1Phase.Preflight;
    internal W1Phase? FailurePhase { get; private set; }
    internal int? HResult { get; private set; }
    internal List<MetadataObservation> Metadata { get; } = new();
    internal List<CleanupObservation> Cleanup { get; } = new();
    internal List<FailureObservation> SecondaryFailures { get; } = new();

    internal void RecordFailure(W1Phase phase, string outcome, int? hResult = null)
    {
        // Preserve the first phase/outcome/code together, including a deliberately
        // absent HRESULT for an observation stop. Later failures cannot replace it.
        if (!FailurePhase.HasValue)
        {
            FailurePhase = phase;
            HResult = hResult;
            Outcome = outcome;
        }
        else SecondaryFailures.Add(new FailureObservation(phase, hResult));
    }

    // No raw bytes, pins, names, subject/IP/serial, paths, handles, exception text,
    // environment, user identity or timestamps enter the shareable representation.
    internal byte[] Render(int maxBytes)
    {
        var text = new StringBuilder();
        text.AppendLine("W1 LIMITED OBSERVATIONS — NOT A SECURITY PASS");
        text.Append("Outcome=").AppendLine(Outcome);
        text.AppendLine("Report=PRE_WRITE_SNAPSHOT;WriteFlushDispose=NOT_ATTESTED");
        text.Append("Phase=").AppendLine(Phase.ToString());
        if (FailurePhase.HasValue) text.Append("FailurePhase=").AppendLine(FailurePhase.Value.ToString());
        text.Append("NativeEntered=").AppendLine(NativeEntered ? "YES" : "NO");
        text.Append("PublicLeafObserved=").AppendLine(PublicLeafObserved ? "YES" : "NO");
        text.AppendLine("ACL=NOT_ESTABLISHED;ExportDenial=NOT_PROVEN;PersistenceAbsence=NOT_PROVEN");
        text.Append("Cleanup=").AppendLine(NativeEntered ? "CleanupUnconfirmed" : "NOT_RUN");
        text.AppendLine("PRODUCTION SECURITY NOT PROVEN;TLS NOT RUN;LAN NOT RUN;ANDROID NOT RUN");
        if (HResult.HasValue) text.Append("ErrorHResult=").AppendLine(Hex(HResult.Value));
        foreach (var item in SecondaryFailures)
        {
            text.Append("SecondaryFailure=").Append(item.Phase);
            if (item.HResult.HasValue) text.Append(';').Append(Hex(item.HResult.Value));
            text.AppendLine();
        }
        foreach (var item in Metadata)
        {
            text.Append(item.Field).Append('=').Append(item.Status);
            if (item.HResult.HasValue) text.Append(';').Append(Hex(item.HResult.Value));
            text.AppendLine();
        }
        foreach (var item in Cleanup)
            text.Append(item.Phase).Append(";Attempted=").Append(item.Attempted)
                .Append(";DisposeReturned=").Append(item.DisposeReturned).AppendLine();
        byte[] result = Encoding.UTF8.GetBytes(text.ToString());
        if (result.Length > maxBytes) throw new InvalidOperationException("W1_REPORT_BOUND");
        return result;
    }

    private static string Hex(int value) => unchecked((uint)value).ToString("X8", CultureInfo.InvariantCulture);
}
