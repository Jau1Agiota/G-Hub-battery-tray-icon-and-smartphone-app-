using System.Collections.Immutable;
using System.Security.Cryptography;

namespace PeripheralBatteryCompanion.Core.TransportSecurity;

/// <summary>Explicit in-memory primitive only: no issuance endpoint, enrollment, persistence, logging or production activation.</summary>
public sealed class Ephemeral256BitSecret : IDisposable
{
    public const int ByteLength = 32;
    private readonly object _gate = new();
    private readonly byte[] _secret;
    private bool _disposed;
    private Ephemeral256BitSecret(byte[] secret) => _secret = secret;
    public static Ephemeral256BitSecret Create() => new(RandomNumberGenerator.GetBytes(ByteLength));
    public void CopyTo(Span<byte> destination)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (destination.Length != ByteLength) throw new ArgumentException("Exactly 32 destination bytes required.");
            _secret.CopyTo(destination);
        }
    }
    public ImmutableArray<byte> ComputeSha256Verifier()
    {
        lock (_gate) { ObjectDisposedException.ThrowIf(_disposed, this); return ImmutableArray.CreateRange(SHA256.HashData(_secret)); }
    }
    public override string ToString() => "Ephemeral256BitSecret (redacted)";
    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; CryptographicOperations.ZeroMemory(_secret); _disposed = true; }
    }
}
