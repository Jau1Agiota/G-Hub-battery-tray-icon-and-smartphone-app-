namespace PeripheralBatteryCompanion.EphemeralCapability;

internal static class Program
{
    private static int Main(string[] args)
    {
        // Preview has no identity/store/provider access and writes only stdout. Not invoked in this stage.
        if (args is ["--preview"]) {
            Console.WriteLine(Consent.Preview(Guid.NewGuid().ToString("N")));
            return 0;
        }
        // No dispatch to the old persisted-key fixture. No native work before exact consent/platform gate.
        if (args.Length != 3 || args[0] != "--owner-approved-ephemeral-only" ||
            !Consent.Allowed(args[1], args[2]) || !OperatingSystem.IsWindows()) {
            Console.WriteLine(CapabilityResult.Stop);
            return 2;
        }
        var result = CapabilityEngine.Run(args[1], args[2], new WindowsCapabilityApi());
        foreach (Phase phase in Enum.GetValues<Phase>()) {
            var items = result.Evidence.Where(e => e.Phase == phase).ToArray();
            if (items.Length == 0) Console.WriteLine($"phase={phase}; status=NotRun");
            foreach (var item in items)
                Console.WriteLine($"phase={phase}; status={item.Outcome}; category={item.Category}; HRESULT={(item.HResult is int code ? $"0x{code:X8}" : "none")}");
        }
        Console.WriteLine($"CAPABILITY={result.Capability}; zeroization={result.Memory}; POLICY PROOF={result.PolicyProof}; no persisted-target conclusion; STOP for owner review.");
        return result.Capability == Outcome.Pass ? 0 : 1;
    }
}
