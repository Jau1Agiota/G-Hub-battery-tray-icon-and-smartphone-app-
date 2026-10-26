namespace PeripheralBatteryCompanion.G305Inspector;

public static class ReceiverFilter
{
    public static bool TryMatch(string? interfacePath, out ReceiverCollection collection)
    {
        collection = default;
        if (interfacePath is null || !interfacePath.StartsWith(@"\\?\hid#", StringComparison.OrdinalIgnoreCase)) return false;
        var parts = interfacePath.Split('#');
        if (parts.Length != 4 || string.IsNullOrWhiteSpace(parts[2]) || !Guid.TryParse(parts[3], out _)) return false;
        var tokens = parts[1].Split('&');
        bool HasExactly(string prefix, string expected) =>
            tokens.Count(token => token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) == 1 &&
            tokens.Contains(expected, StringComparer.OrdinalIgnoreCase);
        if (!HasExactly("vid_", "vid_046d") || !HasExactly("pid_", "pid_c53f") || !HasExactly("mi_", "mi_02")) return false;
        var collections = tokens.Where(token => token.StartsWith("col", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (collections.Length != 1) return false;
        collection = collections[0].ToUpperInvariant() switch
        {
            "COL01" => ReceiverCollection.Col01, "COL02" => ReceiverCollection.Col02,
            "COL03" => ReceiverCollection.Col03, _ => default
        };
        return collection != default;
    }

    public static ushort ExpectedUsage(ReceiverCollection collection) => collection switch
    {
        ReceiverCollection.Col01 => 0x0001, ReceiverCollection.Col02 => 0x0002,
        ReceiverCollection.Col03 => 0x0004, _ => throw new ArgumentOutOfRangeException(nameof(collection))
    };
}
