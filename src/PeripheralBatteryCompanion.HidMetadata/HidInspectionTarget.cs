namespace PeripheralBatteryCompanion.G305Inspector;

// Closed, immutable profiles: callers cannot supply arbitrary VID/PID/interface targets.
public sealed class HidInspectionTarget
{
    public static HidInspectionTarget G305 { get; } = new("G305", "046D", "C53F", "02",
        "Scope: VID 046D / PID C53F / MI_02 / COL01-COL03 (G915 C547 excluded)", null,
        [(0xFF00, 0x0001), (0xFF00, 0x0002), (0xFF00, 0x0004)]);
    public static HidInspectionTarget HyperX { get; } = new("HyperX Cloud III Wireless", "03F0", "05B7", "03",
        "Scope: VID 03F0 / PID 05B7 / MI_03 / COL01-COL03 (COL01 prioritized)", "4109",
        [(0xFF13, 0x0001), (0x000C, 0x0001), (0x000B, 0x0005)]);

    public string Name { get; }
    public string VendorId { get; }
    public string ProductId { get; }
    public string InterfaceNumber { get; }
    public string Scope { get; }
    public string? ReferenceRevision { get; }
    private readonly (ushort Page, ushort Usage)[] _usages;

    private HidInspectionTarget(string name, string vendorId, string productId, string interfaceNumber,
        string scope, string? referenceRevision, (ushort Page, ushort Usage)[] usages)
    {
        Name = name; VendorId = vendorId; ProductId = productId; InterfaceNumber = interfaceNumber;
        Scope = scope; ReferenceRevision = referenceRevision; _usages = usages;
    }

    public (ushort Page, ushort Usage) ExpectedUsage(ReceiverCollection collection)
    {
        var index = (int)collection - 1;
        if (index < 0 || index >= _usages.Length) throw new ArgumentOutOfRangeException(nameof(collection));
        return _usages[index];
    }
}
