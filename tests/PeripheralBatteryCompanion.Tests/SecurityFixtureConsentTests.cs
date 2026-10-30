using Xunit;
using FixtureProgram = PeripheralBatteryCompanion.SecurityFixture.FixtureConsent;

namespace PeripheralBatteryCompanion.Tests;

// Pure argument tests only. Never call Fixture.Run, create keys, certificates or files.
public sealed class SecurityFixtureConsentTests
{
    [Fact]
    public void OnlyExactConfirmationWithNonzeroOperationIsAccepted()
    {
        const string id = "1234567812344234a2341234567890ab";
        Assert.True(FixtureProgram.ValidArguments(["--confirm-fixture-creation", id]));
        Assert.False(FixtureProgram.ValidArguments([]));
        Assert.False(FixtureProgram.ValidArguments([id]));
        Assert.False(FixtureProgram.ValidArguments(["--preview", id]));
        Assert.False(FixtureProgram.ValidArguments(["--CONFIRM-FIXTURE-CREATION", id]));
        Assert.False(FixtureProgram.ValidArguments(["--confirm-fixture-creation", new string('0', 32)]));
        Assert.False(FixtureProgram.ValidArguments(["--confirm-fixture-creation", "../../other"]));
        Assert.False(FixtureProgram.ValidArguments(["--confirm-fixture-creation", "123456781234123412341234567890ab"]));
        Assert.False(FixtureProgram.ValidArguments(["--confirm-fixture-creation", id, "extra"]));
    }
}

public sealed class SecurityFixtureOwnershipTests
{
    [Fact]
    public void CleanupRefusesUnrelatedIdentityOrUnverifiedJournal()
    {
        const string id = "1234567812344234a2341234567890ab";
        string name = "PBC-A22C5-Fixture-" + id;
        Assert.True(FixtureProgram.CanDeleteKey(id, name, [1, 2, 3], [1, 2, 3], true));
        Assert.False(FixtureProgram.CanDeleteKey(id, name + "other", [1, 2, 3], [1, 2, 3], true));
        Assert.False(FixtureProgram.CanDeleteKey(id, "real-user-key", [1, 2, 3], [1, 2, 3], true));
        Assert.False(FixtureProgram.CanDeleteKey(id, name, [1, 2, 3], [3, 2, 1], true));
        Assert.False(FixtureProgram.CanDeleteKey(id, name, [1, 2, 3], [1, 2, 3], false));
        Assert.False(FixtureProgram.CanDeleteKey(id, name, [], [], true));
        Assert.False(FixtureProgram.CanDeleteKey("bad", name, [1], [1], true));
        Assert.True(FixtureProgram.CertificateMatchesOwnedIdentity("owned", "owned", [1, 2], [1, 2]));
        Assert.False(FixtureProgram.CertificateMatchesOwnedIdentity("owned", "other", [1, 2], [1, 2]));
        Assert.False(FixtureProgram.CertificateMatchesOwnedIdentity("owned", "owned", [1, 2], [2, 1]));
        Assert.False(FixtureProgram.CertificateMatchesOwnedIdentity("owned", "owned", [], []));
    }
}
