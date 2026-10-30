namespace PeripheralBatteryCompanion.EphemeralCapability;

internal static class Program
{
    private static int Main(string[] args)
    {
        // Independent Option 2 test-only route. Never falls back to A, B1 or original fixture.
        if (args is ["--preview-option2"]) {
            Console.WriteLine(O2Presentation.Preview(Guid.NewGuid().ToString("N"))); return 0;
        }
        if (args.Length > 0 && args[0] == "--reviewed-option2") {
            var permit = O2Permit.Parse(args);
            if (permit is null || !OperatingSystem.IsWindows()) { Console.WriteLine(O2Presentation.Stop); return 2; }
            var result2 = Option2Engine.Run(permit, new WindowsOption2Api(), new WindowsOption2Journals());
            foreach (string line in O2Presentation.Format(result2)) Console.WriteLine(line);
            return result2.Result == Outcome.Pass ? 0 : result2.Result == Outcome.Inconclusive ? 3 : 1;
        }
        // Contract B is explicitly selected and always returns here; errors never fall back to A.
        if (args is ["--preview-contract-b"]) {
            Console.WriteLine(BPresentation.Preview(Guid.NewGuid().ToString("N")));
            return 0;
        }
        if (args.Length > 0 && args[0] == "--reviewed-contract-b") {
            var permit = BPermit.FromArguments(args);
            if (permit is null || !OperatingSystem.IsWindows()) { Console.WriteLine(BPresentation.Stop); return 2; }
            var resultB = ContractBEngine.Run(permit, WindowsContractBApi.Create());
            foreach (string line in BPresentation.Format(resultB)) Console.WriteLine(line);
            return resultB.Capability == Outcome.Pass ? 0 : 1;
        }
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
                Console.WriteLine($"phase={phase}; status={item.Outcome}; category={item.Category}; property={MetadataProperties.Label(item.Property)}; HRESULT={(item.HResult is int code ? $"0x{code:X8}" : "none")}");
        }
        Console.WriteLine($"CAPABILITY={result.Capability}; zeroization={result.Memory}; POLICY PROOF={result.PolicyProof}; no persisted-target conclusion; STOP for owner review.");
        return result.Capability == Outcome.Pass ? 0 : 1;
    }
}
