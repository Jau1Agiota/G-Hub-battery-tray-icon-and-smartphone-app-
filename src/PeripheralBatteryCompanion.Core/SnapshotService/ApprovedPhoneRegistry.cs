using System.Collections.Immutable;
using System.Security.Cryptography;

namespace PeripheralBatteryCompanion.Core.SnapshotService;

/// <summary>In-memory verifier boundary only: NOT pairing, credential issuance or persistent secret storage.</summary>
public sealed class ApprovedPhoneRegistry
{
    public const int MaxPhones = 16;
    private readonly object _gate = new();
    private readonly Dictionary<Guid, ImmutableArray<byte>> _verifiers = [];

    /// <summary>Future locally approved enrollment supplies SHA256 of a cryptographically random 32-byte token.</summary>
    public void ApproveVerifier(Guid phoneId, ReadOnlySpan<byte> tokenSha256)
    {
        if (phoneId == Guid.Empty || tokenSha256.Length != 32) throw new ArgumentException("Invalid enrollment verifier.");
        lock (_gate)
        {
            if (!_verifiers.ContainsKey(phoneId) && _verifiers.Count >= MaxPhones) throw new InvalidOperationException("Enrollment limit reached.");
            foreach (var entry in _verifiers)
                if (entry.Key != phoneId && CryptographicOperations.FixedTimeEquals(entry.Value.AsSpan(), tokenSha256))
                    throw new ArgumentException("Credential already enrolled.");
            _verifiers[phoneId] = ImmutableArray.CreateRange(tokenSha256.ToArray());
        }
    }
    public bool Revoke(Guid phoneId) { lock (_gate) return _verifiers.Remove(phoneId); }

    // Synchronous trusted-core callback only. Revoke cannot complete while an authorized response is being constructed.
    // A later socket write must not hold this lock or run inside this callback.
    internal T WithAuthorization<T>(string? header, Func<Guid, T> authorized, T denied)
    {
        if (header is null || header.Length != 50 || !header.StartsWith("Bearer ", StringComparison.Ordinal)) return denied;
        var encoded = header.AsSpan(7);
        foreach (var c in encoded) if (!(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')) return denied;
        Span<byte> token = stackalloc byte[32];
        Span<byte> digest = stackalloc byte[32];
        var base64 = header[7..].Replace('-', '+').Replace('_', '/') + "=";
        if (!Convert.TryFromBase64String(base64, token, out var count) || count != 32) return denied;
        // Reject noncanonical padding-bit aliases as malformed credentials.
        if (Convert.ToBase64String(token).TrimEnd('=').Replace('+', '-').Replace('/', '_') != header[7..])
        { CryptographicOperations.ZeroMemory(token); return denied; }
        SHA256.HashData(token, digest);
        CryptographicOperations.ZeroMemory(token);
        try
        {
            lock (_gate)
            {
                Guid? match = null;
                foreach (var entry in _verifiers)
                    if (CryptographicOperations.FixedTimeEquals(entry.Value.AsSpan(), digest)) match = entry.Key;
                return match is { } id ? authorized(id) : denied;
            }
        }
        finally { CryptographicOperations.ZeroMemory(digest); }
    }
}
