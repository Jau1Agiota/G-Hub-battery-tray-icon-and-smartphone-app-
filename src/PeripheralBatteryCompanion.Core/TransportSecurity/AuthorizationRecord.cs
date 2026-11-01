using System.Text;
using System.Text.Json;

namespace PeripheralBatteryCompanion.Core.TransportSecurity;

/// <summary>Verifier-only protected-storage payload. Encoding is NOT encryption or verified storage.</summary>
public sealed class AuthorizationRecord
{
    public EnrollmentIdentity Identity { get; }
    public Guid EnrollmentId { get; }
    public Guid PhoneId { get; }
    public string TokenSha256 { get; }
    public string TranscriptSha256 { get; }
    public long Generation { get; }
    public DateTimeOffset IssuedAt { get; }
    public DateTimeOffset ExpiresAt { get; }
    public bool Revoked { get; }

    public AuthorizationRecord(EnrollmentIdentity identity, Guid enrollmentId, Guid phoneId,
        string tokenSha256, string transcriptSha256, long generation,
        DateTimeOffset issuedAt, DateTimeOffset expiresAt, bool revoked)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (enrollmentId == Guid.Empty || phoneId == Guid.Empty || generation < 1 ||
            !EnrollmentIdentity.IsLowerHexDigest(tokenSha256) || !EnrollmentIdentity.IsLowerHexDigest(transcriptSha256) ||
            !EnrollmentIdentity.WholeUtc(issuedAt) || !EnrollmentIdentity.WholeUtc(expiresAt) ||
            expiresAt <= issuedAt || expiresAt - issuedAt > TimeSpan.FromDays(90) ||
            issuedAt < identity.LeafNotBefore || expiresAt > identity.LeafNotAfter)
            throw new ArgumentException("Invalid authorization record.");
        Identity = identity; EnrollmentId = enrollmentId; PhoneId = phoneId; TokenSha256 = tokenSha256;
        TranscriptSha256 = transcriptSha256; Generation = generation; IssuedAt = issuedAt;
        ExpiresAt = expiresAt; Revoked = revoked;
    }

    public AuthorizationRecord Tombstone() => new(Identity, EnrollmentId, PhoneId, TokenSha256,
        TranscriptSha256, checked(Generation + 1), IssuedAt, ExpiresAt, true);
    public override string ToString() => "AuthorizationRecord (redacted)";
}

/// <summary>Bounded schema-v1 bytes for a future protected store; not a disk or DPAPI adapter.</summary>
public static class AuthorizationRecordCodec
{
    public const int MaxBytes = 8192;
    private static readonly string[] Keys = ["version", "scope", "identityId", "endpoint", "spkiSha256", "leafDer",
        "leafNotBefore", "leafNotAfter", "enrollmentId", "phoneId", "tokenSha256", "transcriptSha256",
        "generation", "issuedAt", "expiresAt", "revoked"];

    public static byte[] Encode(AuthorizationRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", 1); writer.WriteString("scope", "snapshot.read");
            writer.WriteString("identityId", record.Identity.IdentityId.ToString("D"));
            writer.WriteString("endpoint", record.Identity.Endpoint); writer.WriteString("spkiSha256", record.Identity.SpkiSha256);
            writer.WriteBase64String("leafDer", record.Identity.LeafDer.AsSpan());
            writer.WriteNumber("leafNotBefore", record.Identity.LeafNotBefore.ToUnixTimeSeconds());
            writer.WriteNumber("leafNotAfter", record.Identity.LeafNotAfter.ToUnixTimeSeconds());
            writer.WriteString("enrollmentId", record.EnrollmentId.ToString("D")); writer.WriteString("phoneId", record.PhoneId.ToString("D"));
            writer.WriteString("tokenSha256", record.TokenSha256); writer.WriteString("transcriptSha256", record.TranscriptSha256);
            writer.WriteNumber("generation", record.Generation); writer.WriteNumber("issuedAt", record.IssuedAt.ToUnixTimeSeconds());
            writer.WriteNumber("expiresAt", record.ExpiresAt.ToUnixTimeSeconds()); writer.WriteBoolean("revoked", record.Revoked);
            writer.WriteEndObject();
        }
        if (stream.Length > MaxBytes) throw new InvalidOperationException("Authorization record exceeds bound.");
        return stream.ToArray();
    }

    public static bool TryDecode(ReadOnlyMemory<byte> bytes, EnrollmentIdentity expectedIdentity, out AuthorizationRecord? record)
    {
        record = null;
        if (expectedIdentity is null || bytes.Length is < 1 or > MaxBytes) return false;
        try
        {
            bytes = bytes.ToArray();
            // Reject malformed UTF-8, BOM, comments, trailing data and duplicate/unknown fields.
            if (bytes.Span.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) return false;
            _ = new UTF8Encoding(false, true).GetCharCount(bytes.Span);
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 2 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            var found = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
                if (!Keys.Contains(property.Name, StringComparer.Ordinal) || !found.Add(property.Name)) return false;
            if (found.Count != Keys.Length || root.GetProperty("version").GetInt32() != 1 ||
                root.GetProperty("scope").GetString() != "snapshot.read") return false;
            Guid Id(string name)
            {
                string? text = root.GetProperty(name).GetString();
                if (!Guid.TryParseExact(text, "D", out var id) || id == Guid.Empty || text != id.ToString("D")) throw new FormatException();
                return id;
            }
            DateTimeOffset Time(string name) => DateTimeOffset.FromUnixTimeSeconds(root.GetProperty(name).GetInt64());
            string leafText = root.GetProperty("leafDer").GetString() ?? throw new FormatException();
            if (leafText.Length > 5464) return false;
            byte[] leaf = Convert.FromBase64String(leafText);
            if (Convert.ToBase64String(leaf) != leafText) return false;
            var identity = new EnrollmentIdentity(Id("identityId"), root.GetProperty("endpoint").GetString()!,
                root.GetProperty("spkiSha256").GetString()!, leaf, Time("leafNotBefore"), Time("leafNotAfter"));
            if (!expectedIdentity.Matches(identity)) return false;
            record = new(identity, Id("enrollmentId"), Id("phoneId"), root.GetProperty("tokenSha256").GetString()!,
                root.GetProperty("transcriptSha256").GetString()!, root.GetProperty("generation").GetInt64(),
                Time("issuedAt"), Time("expiresAt"), root.GetProperty("revoked").GetBoolean());
            return true;
        }
        catch (Exception error) when (error is JsonException or ArgumentException or FormatException or InvalidOperationException or OverflowException)
        {
            record = null;
            return false;
        }
    }
}
