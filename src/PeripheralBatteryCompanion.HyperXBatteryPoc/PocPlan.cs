namespace PeripheralBatteryCompanion.HyperXBatteryPoc;

public enum PocMode { Inspect, PassiveCoexistence, Preview, ReadBattery, Invalid }
public static class PocPlan
{
    public static PocMode Parse(string[] args)
    {
        if (args.Length == 1 && args[0] == "--passive-coexistence") return PocMode.PassiveCoexistence;
        if (args.Length == 0) return PocMode.Inspect;
        if (args.Length == 1 && args[0] == "--preview-request") return PocMode.Preview;
        if (args.Length == 2 && args.Distinct(StringComparer.Ordinal).Count() == 2 &&
            args.Contains("--read-battery", StringComparer.Ordinal) && args.Contains("--confirm-ngenuity-closed", StringComparer.Ordinal))
            return PocMode.ReadBattery;
        return PocMode.Invalid;
    }

    public static bool IsNgenuityName(string name) => name.Contains("ngenuity", StringComparison.OrdinalIgnoreCase);
}
