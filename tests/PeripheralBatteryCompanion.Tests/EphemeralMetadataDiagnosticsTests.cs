using System.Text;
using PeripheralBatteryCompanion.EphemeralCapability;
using Xunit;

namespace PeripheralBatteryCompanion.Tests;

public sealed class EphemeralMetadataDiagnosticsTests
{
    [Fact]
    public void ApplicabilityClassificationDoesNotConfuseProviderAndKeyNameOrRemoveGates()
    {
        Assert.Equal(MetadataObject.Provider, MetadataProperties.Describe(MetadataProperty.ProviderName).Object);
        Assert.Equal(MetadataObject.Key, MetadataProperties.Describe(MetadataProperty.KeyName).Object);
        Assert.Equal("Name", MetadataProperties.Describe(MetadataProperty.ProviderName).WireName);
        Assert.Equal("Name", MetadataProperties.Describe(MetadataProperty.KeyName).WireName);
        Assert.Equal(PropertyApplicability.PersistentKeysOnly, MetadataProperties.Describe(MetadataProperty.KeyUiPolicy).Applicability);
        Assert.Equal(PropertyApplicability.PersistedKeyWording, MetadataProperties.Describe(MetadataProperty.KeyExportPolicy).Applicability);
        foreach (var property in new[] { MetadataProperty.KeyAlgorithmName, MetadataProperty.KeyAlgorithmGroup,
            MetadataProperty.KeyLength, MetadataProperty.KeyType, MetadataProperty.KeyUsage })
            Assert.Equal(PropertyApplicability.KeyGeneral, MetadataProperties.Describe(property).Applicability);
        Assert.Throws<ArgumentOutOfRangeException>(() => MetadataProperties.Describe((MetadataProperty)999));
    }

    [Fact]
    public void TextAndDwordErrorsCarryOnlyFixedPropertyNotNativeValue()
    {
        foreach (var property in Enum.GetValues<MetadataProperty>()) {
            Assert.Equal(property.ToString(), MetadataProperties.Label(property));
            var error = Assert.Throws<MetadataReadFailure>(() => MetadataProperties.ParseDword(property, [1, 2, 3]));
            Assert.Equal(property, error.Property);
            Assert.Equal(MetadataReadError.MalformedValue, error.Error);
            Assert.Null(error.Code);
        }
        Assert.Equal("UnknownMetadataProperty", MetadataProperties.Label((MetadataProperty)999));
        Assert.Equal("None", MetadataProperties.Label(null));
        Assert.Equal(3u, MetadataProperties.ParseDword(MetadataProperty.KeyExportPolicy, [3, 0, 0, 0]));
        Assert.Equal(256, MetadataProperties.ParseBitLength(256));
        Assert.Equal(MetadataProperty.KeyLength, Assert.Throws<MetadataReadFailure>(() => MetadataProperties.ParseBitLength(uint.MaxValue)).Property);
        Assert.Equal("", MetadataProperties.ParseText(MetadataProperty.KeyName, []));
        Assert.Throws<MetadataReadFailure>(() => MetadataProperties.ParseText(MetadataProperty.ProviderName, [1]));
        Assert.Throws<MetadataReadFailure>(() => MetadataProperties.ParseText(MetadataProperty.ProviderName, [1, 2]));
        const string secret = "SECRET user-path SID key-name";
        Assert.Equal(secret, MetadataProperties.ParseText(MetadataProperty.KeyName, Encoding.Unicode.GetBytes(secret + "\0")));
        // Decision code may read a value; diagnostics cannot retain/output it.
        var evidence = new Evidence(Phase.Metadata, Outcome.Fail, Category: "MalformedMetadata", Property: MetadataProperty.KeyName);
        Assert.DoesNotContain(secret, evidence.ToString());
    }
}
