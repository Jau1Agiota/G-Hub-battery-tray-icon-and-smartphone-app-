using PeripheralBatteryCompanion.Windows;
using Xunit;
using System.Security.AccessControl;
using System.Security.Principal;

namespace PeripheralBatteryCompanion.Windows.Tests;

public sealed class RecordProtectionBoundaryTests
{
    [Fact]
    public void CurrentUserAdapterDeniesBeforeSerializationAllocationOrNativeCalls()
    {
        var adapter = new WindowsCurrentUserRecordProtection();
        var protect = Assert.Throws<InvalidOperationException>(() => adapter.Protect(null!));
        var unprotect = Assert.Throws<InvalidOperationException>(() => adapter.Unprotect(null!, null!));
        Assert.Contains("GATE E BLOCKED", protect.Message);
        Assert.Contains("GATE E BLOCKED", unprotect.Message);
        Assert.Contains("GATE E BLOCKED", Assert.Throws<InvalidOperationException>(() => adapter.ProtectJournalEnvelope(null!, null!)).Message);
        Assert.Contains("GATE E BLOCKED", Assert.Throws<InvalidOperationException>(() => adapter.UnprotectJournalEnvelope(null!, null!)).Message);
        // Deliberately no reflection into Transform, gate removal, DPAPI, key or certificate operation.
    }

    [Fact]
    public void ProductionJournalAndPersistedKeyDenyBeforeFilesystemAclOrCngAccess()
    {
        Assert.Contains("GATE E BLOCKED", Assert.Throws<InvalidOperationException>(() => WindowsAuthorizationJournal.OpenExisting(null!, null!)).Message);
        Assert.Contains("GATE E BLOCKED", Assert.Throws<InvalidOperationException>(() => WindowsAuthorizationJournal.CreateFreshByLocalOwner(null!, null!)).Message);
        Assert.Contains("GATE E BLOCKED", Assert.Throws<InvalidOperationException>(() => WindowsAuthorizationJournal.OpenCoordinatorForLocalReview(null!, null!)).Message);
        Assert.Contains("GATE E BLOCKED", Assert.Throws<InvalidOperationException>(() => WindowsAuthorizationDirectory.OpenExisting(null!)).Message);
        Assert.Contains("GATE E BLOCKED", Assert.Throws<InvalidOperationException>(() => WindowsAuthorizationDirectory.CreateFresh(null!)).Message);
        Assert.Contains("GATE E BLOCKED", Assert.Throws<InvalidOperationException>(() => WindowsPersistentIdentityCustody.CreateByLocalOwner(Guid.Empty)).Message);
        Assert.Contains("GATE E BLOCKED", Assert.Throws<InvalidOperationException>(() => WindowsPersistentIdentityCustody.OpenReviewed(null!)).Message);
    }

    [Theory]
    [InlineData(@"C:\Synthetic\Journal", true)]
    [InlineData(@"C:\Synthetic\Journal\", false)]
    [InlineData(@"C:\Synthetic\..\Journal", false)]
    [InlineData(@"C:\Synthetic\Journal.", false)]
    [InlineData(@"C:\Synthetic\Journal ", false)]
    [InlineData(@"C:\Synthetic\Journal:stream", false)]
    [InlineData(@"\\server\Synthetic\Journal", false)]
    [InlineData(@"\\?\C:\Synthetic\Journal", false)]
    [InlineData(@"C:Journal", false)]
    [InlineData(@"C:/Synthetic/Journal", false)]
    [InlineData(@"C:\Synthetic\NUL", false)]
    [InlineData(@"C:\Synthetic\COM1.txt", false)]
    [InlineData(@"C:\Synthetic\LPT¹", false)]
    public void JournalPathSyntaxRejectsAliasesWithoutOpeningPaths(string path, bool valid) =>
        Assert.Equal(valid, WindowsAuthorizationDirectory.ValidPathSyntax(path));

    [Theory]
    [InlineData("valid", true)]
    [InlineData("otherOwner", false)]
    [InlineData("extraAce", false)]
    [InlineData("deny", false)]
    [InlineData("inheritOnly", false)]
    [InlineData("unprotected", false)]
    [InlineData("rights", false)]
    [InlineData("absentDacl", false)]
    public void OwnerOnlyDescriptorPolicyRejectsBroadOrAmbiguousAcl(string mutation, bool accepted)
    {
        // Synthetic binary SIDs and managed ACL objects only. No account lookup,
        // permission change, file/security observation, DPAPI or CNG call.
        var owner = new SecurityIdentifier(new byte[] { 1, 1, 0, 0, 0, 0, 0, 5, 21, 0, 0, 0 }, 0);
        var other = new SecurityIdentifier(new byte[] { 1, 1, 0, 0, 0, 0, 0, 5, 22, 0, 0, 0 }, 0);
        RawSecurityDescriptor Descriptor(int mask)
        {
            var acl = new RawAcl(2, 2);
            acl.InsertAce(0, new CommonAce(mutation == "inheritOnly" ? AceFlags.InheritOnly : AceFlags.None,
                mutation == "deny" ? AceQualifier.AccessDenied : AceQualifier.AccessAllowed,
                mutation == "rights" ? 1 : mask, owner, false, null));
            if (mutation == "extraAce") acl.InsertAce(1, new CommonAce(AceFlags.None, AceQualifier.AccessAllowed, mask, other, false, null));
            return new RawSecurityDescriptor((mutation == "absentDacl" ? ControlFlags.None : ControlFlags.DiscretionaryAclPresent) |
                (mutation == "unprotected" ? ControlFlags.None : ControlFlags.DiscretionaryAclProtected),
                mutation == "otherOwner" ? other : owner, null, null, acl);
        }
        Assert.Equal(accepted, WindowsAuthorizationDirectory.OwnerOnly(Descriptor((int)FileSystemRights.FullControl), owner, true));
        Assert.Equal(accepted, WindowsPersistentIdentityCustody.OwnerOnlyDescriptor(Descriptor(0x001f019b), owner));
    }
}
