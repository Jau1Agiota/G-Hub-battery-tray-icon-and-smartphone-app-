namespace PeripheralBatteryCompanion.SecurityFixture;

// Pure guard shared with portable tests. It performs no native calls or writes.
internal static class FixtureConsent
{
    internal static bool ValidArguments(string[] args) => args.Length == 2 &&
        args[0] == "--confirm-fixture-creation" && ValidOperation(args[1]);

    private static bool ValidOperation(string operation) =>
        Guid.TryParseExact(operation, "N", out var id) && id != Guid.Empty &&
        operation[12] == '4' && "89abAB".Contains(operation[16]);

    internal static bool CertificateMatchesOwnedIdentity(string expectedThumbprint, string actualThumbprint,
        byte[] expectedDer, byte[] actualDer) => expectedThumbprint.Length > 0 &&
        expectedThumbprint == actualThumbprint && expectedDer.Length > 0 && expectedDer.AsSpan().SequenceEqual(actualDer);

    internal static bool CanDeleteKey(string operation, string? actualName, byte[] expectedSpki,
        byte[] actualSpki, bool journalVerified) => journalVerified && ValidOperation(operation) &&
        actualName == "PBC-A22C5-Fixture-" + operation && expectedSpki.Length > 0 &&
        expectedSpki.AsSpan().SequenceEqual(actualSpki);
}
