namespace PeripheralBatteryCompanion.HyperXBatteryPoc;

public enum PocMode { Inspect, PassiveCoexistence, Preview, ReadBattery, ActiveCoexistence, Invalid }
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
        if (args.Length == 2 && args.Distinct(StringComparer.Ordinal).Count() == 2 &&
            args.Contains("--active-coexistence", StringComparer.Ordinal) && args.Contains("--acknowledge-coexistence-risk", StringComparer.Ordinal))
            return PocMode.ActiveCoexistence;
        return PocMode.Invalid;
    }

    // Fail closed if process inspection fails; coexistence requires an observed NGENUITY process.
    public static AttemptStatus? ActiveGuard(PocMode mode, AttemptStatus? observed) => mode switch
    {
        PocMode.ReadBattery => observed,
        PocMode.ActiveCoexistence => observed == AttemptStatus.NgenuityRunning ? null : observed ?? AttemptStatus.NgenuityNotObserved,
        _ => AttemptStatus.UnexpectedFailure
    };

    public static bool IsNgenuityName(string name) => name.Contains("ngenuity", StringComparison.OrdinalIgnoreCase);
}
