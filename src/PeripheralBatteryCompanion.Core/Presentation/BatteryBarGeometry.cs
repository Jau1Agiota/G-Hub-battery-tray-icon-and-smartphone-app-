namespace PeripheralBatteryCompanion.Core.Presentation;

public static class BatteryBarGeometry
{
    // Recomputed from current control width on every paint; precision does not change fill.
    public static float FillWidth(int width, int? percentage) => percentage is >= 0 and <= 100
        ? Math.Max(0, width - 4) * percentage.Value / 100f : 0;
}
