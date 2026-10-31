namespace PeripheralBatteryCompanion.EphemeralCapability;

internal sealed record O2AclEvidence(bool OwnerMatches, bool Protected, int RuleCount,
    bool OnlyOwnerPrincipal, bool AllowRule, bool FullControl);
internal static class Option2JournalSecurity
{
    // Evaluates observations only; Windows adapter obtains actual descriptor/owner/rules. No ACL repair.
    internal static bool Restricted(O2AclEvidence value, bool requireProtected) => value.OwnerMatches &&
        (!requireProtected || value.Protected) && value.RuleCount == 1 && value.OnlyOwnerPrincipal &&
        value.AllowRule && value.FullControl;
}
