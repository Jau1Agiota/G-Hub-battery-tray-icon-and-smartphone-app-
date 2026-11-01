using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using PeripheralBatteryCompanion.Core.Snapshots;
using PeripheralBatteryCompanion.Core.SnapshotService;
using PeripheralBatteryCompanion.Core.TransportSecurity;
using Xunit;

namespace PeripheralBatteryCompanion.Tests;

// All credentials, identities and peer observations are synthetic. Real temporary
// file protocol; deliberately non-encrypting TEST protection, no native key/store calls.
internal sealed class SecurityProtocolFixture : IDisposable
{
    internal static readonly DateTimeOffset Now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
    internal static readonly Guid EnrollmentId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    internal static readonly Guid PhoneId = Guid.Parse("33333333-3333-4333-8333-333333333333");
    internal static readonly Guid Publisher = Guid.Parse("44444444-4444-4444-8444-444444444444");
    internal static byte[] Token => Enumerable.Repeat((byte)0x42, 32).ToArray();
    internal static byte[] Bootstrap => Enumerable.Repeat((byte)0x24, 32).ToArray();
    internal static string Encoded(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    internal static string Bearer => "Bearer " + Encoded(Token);
    internal static string BootstrapHeader => "Bearer " + Encoded(Bootstrap);
    internal static string Nonce(int value)
    { var bytes = new byte[32]; BitConverter.TryWriteBytes(bytes.AsSpan(), value); return Encoded(bytes); }
    internal EnrollmentIdentity Identity { get; } = new(Guid.Parse("11111111-1111-4111-8111-111111111111"),
        "https://192.168.1.10:8443", new string('a', 64), [1, 2, 3], Now.AddDays(-1), Now.AddDays(89));
    internal EnrollmentClaim Claim => new(EnrollmentId, PhoneId, Nonce(900), Convert.ToHexString(SHA256.HashData(Token)).ToLowerInvariant());
    internal PeerBinding Peer => new(Identity.Endpoint, Identity.IdentityId, Identity.LeafDer.AsSpan());
    internal Clock Time { get; } = new();
    internal FakePeerVerifier Proof { get; } = new();
    internal DurableAuthorizationJournal Journal { get; private set; }
    internal EnrollmentAuthorization Authorization { get; private set; }
    internal PublishedSnapshotStore Publications { get; }
    internal AuthorizationRecord Record => Journal.Read().Record!;
    internal string Root { get; } = Path.Join(Path.GetTempPath(), "PBC-ProtocolTests-" + Guid.NewGuid().ToString("N"));
    internal bool FailNextStateWrite;
    internal SecurityProtocolFixture(bool active = true, EnrollmentIdentity? identity = null)
    {
        if (identity is not null) Identity = identity;
        Directory.CreateDirectory(Root);
        Journal = CreateJournal(); Assert.True(Journal.InitializeFreshByLocalOwner());
        Authorization = new(Identity, Journal, Time);
        Publications = new(Publisher, Time); Publications.Publish(BatterySnapshotProjection.Build(null, null, new(Publisher, 0, null)));
        if (active)
        {
            using var pairing = Pairing();
            Assert.True(pairing.OpenByLocalOwner(EnrollmentId, SHA256.HashData(Bootstrap)));
            Assert.True(pairing.TryClaim(Peer, BootstrapHeader, Claim));
            Assert.True(pairing.ConfirmByLocalOwner(Claim.TranscriptSha256(Identity)));
            Assert.True(Authorization.ApproveExposureByLocalOwner());
        }
    }
    private DurableAuthorizationJournal CreateJournal() => new(Identity,
        new AuthorizationJournalFileStorage(Root, (handle, path) => { Assert.False(handle.IsInvalid); Assert.Equal(Root, Path.GetDirectoryName(path)); },
            () => { }, (path, mode, access, share, options) =>
            {
                if (FailNextStateWrite && mode == FileMode.CreateNew && Path.GetFileName(path) == "state.next")
                { FailNextStateWrite = false; throw new IOException("synthetic write failure"); }
                return new FileStream(path, mode, access, share, 4096, options);
            }), new TestProtection());
    internal void Restart() { Authorization.Dispose(); Journal = CreateJournal(); Authorization = new(Identity, Journal, Time); }
    internal PairingSession Pairing(bool realDefaultDenial = false) => new(Identity, Authorization, Time, realDefaultDenial ? null : Proof);
    internal AuthenticatedSnapshotCoordinator Server(bool realDefaultDenial = false) => new(Identity, Authorization, Publications, Time, realDefaultDenial ? null : Proof);
    internal DurableSnapshotReceiver Receiver() => new(Identity, Record, Journal, Time, Proof);
    internal static KeyValuePair<string, string>[] RequestHeaders(string nonce) =>
        new Dictionary<string, string> { ["Authorization"] = Bearer, ["PBC-Protocol"] = "1", ["PBC-Request-Nonce"] = nonce }.ToArray();
    internal AuthenticatedSnapshotResponse Response(Guid instance, long revision, string nonce)
    {
        var node = JsonNode.Parse(Publications.ReadLatest()!.Json.AsSpan())!.AsObject();
        node["serverInstanceId"] = instance.ToString("D"); node["revision"] = revision;
        byte[] body = System.Text.Encoding.UTF8.GetBytes(node.ToJsonString());
        return new(body, new Dictionary<string, string>
        {
            ["PBC-Protocol"] = "1", ["PBC-Request-Nonce"] = nonce, ["PBC-Publisher-Instance"] = instance.ToString("D"),
            ["PBC-Authorization-Generation"] = "1", ["PBC-Snapshot-SHA256"] = Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant(),
            ["Content-Type"] = "application/json", ["Content-Length"] = body.Length.ToString(CultureInfo.InvariantCulture), ["Cache-Control"] = "no-store"
        }.ToArray());
    }
    public void Dispose()
    {
        Authorization.Dispose(); Publications.Dispose();
        if (!Path.GetFullPath(Root).StartsWith(Path.Join(Path.GetTempPath(), "PBC-ProtocolTests-"), StringComparison.Ordinal) ||
            new DirectoryInfo(Root).Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Test cleanup target rejected.");
        Directory.Delete(Root, true);
    }
    internal sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Utc = Now; internal long Tick = TimeSpan.TicksPerHour;
        public override DateTimeOffset GetUtcNow() => Utc;
        public override long GetTimestamp() => Tick;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        internal void Advance(TimeSpan duration) { Utc += duration; Tick += duration.Ticks; }
    }
    internal sealed class FakePeerVerifier : IPeerCertificateVerifier
    {
        internal bool Allow = true;
        internal Func<EnrollmentIdentity, PeerBinding, DateTimeOffset, bool>? Check;
        public bool IsAuthenticatedPeer(EnrollmentIdentity expected, PeerBinding observed, DateTimeOffset now) => Allow && (Check?.Invoke(expected, observed, now) ?? true);
    }
    private sealed class TestProtection : IAuthorizationEnvelopeProtection
    {
        public byte[] Protect(byte[] plaintext) => new byte[] { 0x54 }.Concat(plaintext).ToArray();
        public byte[] Unprotect(byte[] ciphertext) => ciphertext[0] == 0x54 ? ciphertext[1..] : throw new IOException();
    }
}
