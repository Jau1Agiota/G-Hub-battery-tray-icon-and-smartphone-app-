using PeripheralBatteryCompanion.Windows;
using Xunit;

namespace PeripheralBatteryCompanion.Windows.Tests;

// No key is ever created. Missing fixture selection uses an unguessable, disposable name, not a real user's key.
public sealed class ReadOnlyFixtureKeyInspectionTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("existing-user-key")]
    [InlineData("PBC-A22C4-Fixture-00000000000000000000000000000000")]
    [InlineData("PBC-A22C4-Fixture-11111111111111111111111111111111/other")]
    public void InvalidSelectionDoesNotOpenKey(string? name)
    {
        var result = ReadOnlyFixtureKeyInspection.Inspect(name);
        Assert.Equal(FixtureKeyInspectionStatus.InvalidSelection, result.Status);
        Assert.Null(result.ExportPolicy);
        Assert.Null(result.IsMachineKey);
        Assert.Null(result.IsSoftwareProvider);
        Assert.Null(result.IsEcdsa);
        Assert.Null(result.KeySize);
    }

    [Fact]
    public void MissingRandomFixtureDoesNotCreateOrClaimProof()
    {
        var name = ReadOnlyFixtureKeyInspection.Prefix + Guid.NewGuid().ToString("N");
        Assert.True(ReadOnlyFixtureKeyInspection.IsFixtureName(name));
        var result = ReadOnlyFixtureKeyInspection.Inspect(name);
        Assert.Equal(FixtureKeyInspectionStatus.Unavailable, result.Status);
        Assert.Null(result.ExportPolicy);
        Assert.Null(result.IsMachineKey);
        Assert.Null(result.KeySize);
        // Second open must still fail: no lazy provisioning or missing-key replacement.
        Assert.Equal(FixtureKeyInspectionStatus.Unavailable, ReadOnlyFixtureKeyInspection.Inspect(name).Status);
    }
}
