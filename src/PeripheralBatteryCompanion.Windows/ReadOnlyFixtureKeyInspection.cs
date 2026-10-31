using System.Security.Cryptography;

namespace PeripheralBatteryCompanion.Windows;

internal enum FixtureKeyInspectionStatus { InvalidSelection, Unavailable, MetadataRead }
internal sealed record FixtureKeyMetadata(FixtureKeyInspectionStatus Status, bool? IsMachineKey = null,
    bool? IsSoftwareProvider = null, bool? IsEcdsa = null, int? KeySize = null,
    CngExportPolicies? ExportPolicy = null);

/// <summary>
/// Explicit diagnostic only, never registered in startup. Opens only an exactly selected fixture key.
/// Metadata is NOT proof of nonexportability, possession, certificate association or restricted ACLs.
/// No create, delete, sign, export, UI prompt, property write or certificate-store operation.
/// </summary>
internal static class ReadOnlyFixtureKeyInspection
{
    internal const string Prefix = "PBC-A22C4-Fixture-";

    internal static bool IsFixtureName(string? name) => name is not null &&
        name.Length == Prefix.Length + 32 && name.StartsWith(Prefix, StringComparison.Ordinal) &&
        Guid.TryParseExact(name.AsSpan(Prefix.Length), "N", out var id) && id != Guid.Empty;

    internal static FixtureKeyMetadata Inspect(string? exactFixtureName)
    {
        if (!IsFixtureName(exactFixtureName)) return new(FixtureKeyInspectionStatus.InvalidSelection);
        try
        {
            // Explicit CurrentUser software KSP and silent open; no MachineKey option or fallback provider.
            using var key = CngKey.Open(exactFixtureName!, CngProvider.MicrosoftSoftwareKeyStorageProvider,
                CngKeyOpenOptions.Silent);
            return new(FixtureKeyInspectionStatus.MetadataRead, key.IsMachineKey,
                key.Provider == CngProvider.MicrosoftSoftwareKeyStorageProvider,
                key.AlgorithmGroup == CngAlgorithmGroup.ECDsa, key.KeySize, key.ExportPolicy);
        }
        catch (Exception error) when (error is CryptographicException or UnauthorizedAccessException or
            PlatformNotSupportedException or InvalidOperationException)
        {
            // Do not mislabel access denied/provider failure as "key missing", or reveal paths/error contents.
            return new(FixtureKeyInspectionStatus.Unavailable);
        }
    }
}
