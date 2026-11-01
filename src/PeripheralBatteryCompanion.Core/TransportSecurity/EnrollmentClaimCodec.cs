using System.Text;
using System.Text.Json;

namespace PeripheralBatteryCompanion.Core.TransportSecurity;

public static class EnrollmentClaimCodec
{
    public const int MaximumBytes = 1024;
    public static bool TryDecode(ReadOnlyMemory<byte> input, out EnrollmentClaim? claim)
    {
        claim = null;
        if (input.Length is < 1 or > MaximumBytes) return false;
        try
        {
            byte[] bytes = input.ToArray(); _ = new UTF8Encoding(false, true).GetCharCount(bytes);
            using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 2 });
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;
            string[] keys = ["enrollmentId", "phoneId", "phoneNonce", "tokenSha256"];
            var found = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in doc.RootElement.EnumerateObject())
                if (!keys.Contains(item.Name, StringComparer.Ordinal) || !found.Add(item.Name) || item.Value.ValueKind != JsonValueKind.String) return false;
            if (found.Count != 4) return false;
            string Text(string key) => doc.RootElement.GetProperty(key).GetString()!;
            Guid Id(string key)
            {
                string text = Text(key);
                if (!Guid.TryParseExact(text, "D", out var id) || id == Guid.Empty || id.ToString("D") != text) throw new FormatException();
                return id;
            }
            claim = new(Id("enrollmentId"), Id("phoneId"), Text("phoneNonce"), Text("tokenSha256"));
            return true;
        }
        catch (Exception error) when (error is JsonException or ArgumentException or FormatException or InvalidOperationException) { return false; }
    }
}
