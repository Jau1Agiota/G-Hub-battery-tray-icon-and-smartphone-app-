using System.Buffers.Binary;
using System.Text;

namespace PeripheralBatteryCompanion.EphemeralCapability;

internal enum MetadataProperty { ProviderName, KeyAlgorithmName, KeyAlgorithmGroup, KeyUiPolicy, KeyName,
    KeyLength, KeyExportPolicy, KeyType, KeyUsage }
internal enum MetadataObject { Provider, Key }
internal enum PropertyApplicability { ObjectGeneral, KeyGeneral, PersistentKeysOnly, PersistedKeyWording }
internal enum MetadataReadError { NativeStatus, MalformedValue, UnexpectedException }
internal sealed record MetadataPropertyInfo(string WireName, MetadataObject Object, PropertyApplicability Applicability);

// Documentary classification does NOT skip a read or mark unsupported metadata as valid/not-applicable.
internal static class MetadataProperties
{
    internal static MetadataPropertyInfo Describe(MetadataProperty property) => property switch {
        MetadataProperty.ProviderName => new("Name", MetadataObject.Provider, PropertyApplicability.ObjectGeneral),
        MetadataProperty.KeyAlgorithmName => new("Algorithm Name", MetadataObject.Key, PropertyApplicability.KeyGeneral),
        MetadataProperty.KeyAlgorithmGroup => new("Algorithm Group", MetadataObject.Key, PropertyApplicability.KeyGeneral),
        MetadataProperty.KeyUiPolicy => new("UI Policy", MetadataObject.Key, PropertyApplicability.PersistentKeysOnly),
        MetadataProperty.KeyName => new("Name", MetadataObject.Key, PropertyApplicability.ObjectGeneral),
        MetadataProperty.KeyLength => new("Length", MetadataObject.Key, PropertyApplicability.KeyGeneral),
        MetadataProperty.KeyExportPolicy => new("Export Policy", MetadataObject.Key, PropertyApplicability.PersistedKeyWording),
        MetadataProperty.KeyType => new("Key Type", MetadataObject.Key, PropertyApplicability.KeyGeneral),
        MetadataProperty.KeyUsage => new("Key Usage", MetadataObject.Key, PropertyApplicability.KeyGeneral),
        _ => throw new ArgumentOutOfRangeException(nameof(property))
    };
    internal static uint ParseDword(MetadataProperty property, ReadOnlySpan<byte> value)
    {
        if (value.Length != 4) throw new MetadataReadFailure(property, MetadataReadError.MalformedValue);
        return BinaryPrimitives.ReadUInt32LittleEndian(value);
    }
    internal static int ParseBitLength(uint value)
    {
        if (value > int.MaxValue) throw new MetadataReadFailure(MetadataProperty.KeyLength, MetadataReadError.MalformedValue);
        return (int)value;
    }
    internal static string ParseText(MetadataProperty property, ReadOnlySpan<byte> value)
    {
        if (value.Length == 0) return "";
        if (value.Length < 2 || value.Length % 2 != 0 || value[^1] != 0 || value[^2] != 0)
            throw new MetadataReadFailure(property, MetadataReadError.MalformedValue);
        return Encoding.Unicode.GetString(value[..^2]);
    }
    internal static string Label(MetadataProperty? property) => property is null ? "None" :
        Enum.IsDefined(property.Value) ? property.Value.ToString() : "UnknownMetadataProperty";
}
internal sealed class MetadataReadFailure(MetadataProperty property, MetadataReadError error, int? code = null) : Exception
{
    internal MetadataProperty Property { get; } = property;
    internal MetadataReadError Error { get; } = error;
    internal int? Code { get; } = code;
}
