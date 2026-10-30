namespace PeripheralBatteryCompanion.Core.ConnectionFoundations;

// Metadata-only models: NOT security evidence, tokens, native TLS or a listener adapter.
public readonly record struct ClockSample(long UtcSeconds, long MonotonicTicks, bool Certain);
public sealed class ClockGuard
{
    private ClockSample? previous;
    public bool Observe(ClockSample now)
    {
        if (!now.Certain || now.MonotonicTicks < 0 || previous is { } p &&
            (now.UtcSeconds < p.UtcSeconds || now.MonotonicTicks < p.MonotonicTicks)) return false;
        previous = now; return true;
    }
}
public enum ConsentState { Off, Approved, Stopped, Shutdown }
public sealed class ConsentGate
{
    public ConsentState State { get; private set; }
    public void LocalDecision(bool consent, bool prerequisitesKnownValid) =>
        State = State == ConsentState.Shutdown ? State : consent && prerequisitesKnownValid ? ConsentState.Approved : ConsentState.Stopped;
    public bool Evaluate(bool prerequisitesKnownValid)
    {
        if (!prerequisitesKnownValid && State == ConsentState.Approved) State = ConsentState.Stopped;
        return State == ConsentState.Approved;
    }
    public void Restart() { if (State != ConsentState.Shutdown) State = ConsentState.Off; }
    public void Shutdown() => State = ConsentState.Shutdown;
}
public sealed record AdmissionLimits(int Concurrent, int HeaderBytes, int BodyBytes, long DurationTicks)
{
    public bool Valid => Concurrent > 0 && HeaderBytes > 0 && BodyBytes >= 0 && DurationTicks > 0;
}
public sealed class AdmissionBudget(AdmissionLimits limits)
{
    private readonly object gate = new();
    private readonly HashSet<long> active = [];
    private long sequence;
    // No socket, timer, queue or scheduling. Caller must pass elapsed time from its own monotonic source.
    public long? Enter(bool consent, int headers, int body, long elapsed)
    {
        lock (gate) {
            if (!consent || !limits.Valid || headers < 0 || body < 0 || elapsed < 0 ||
                headers > limits.HeaderBytes || body > limits.BodyBytes || elapsed >= limits.DurationTicks ||
                active.Count >= limits.Concurrent || sequence == long.MaxValue) return null;
            var id = ++sequence; active.Add(id); return id;
        }
    }
    public bool Leave(long id) { lock (gate) return active.Remove(id); }
}
public enum SanKind { Ip, Dns }
public sealed record EndpointExpectation(string Host, int Port, SanKind Kind, string AnchorSha256, string SpkiSha256);
public sealed record SyntheticTrust(string Host, int Port, SanKind Kind, string San,
    string AnchorSha256, string SpkiSha256, long NotBefore, long NotAfter, bool PathValid, bool ServerEku);
public static class TrustMetadataPolicy
{
    private static bool Digest(string x) => x.Length == 64 && x.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    public static bool Decide(EndpointExpectation expected, SyntheticTrust seen, ClockSample now)
    {
        // DNS is deliberately unsupported in this stage. No DNS calls or wildcard/CN interpretation.
        if (!now.Certain || expected.Kind != SanKind.Ip || expected.Port is < 1024 or > 65535 ||
            !System.Net.IPAddress.TryParse(expected.Host, out var ip) || ip.ToString() != expected.Host) return false;
        var b = ip.GetAddressBytes();
        var privateIp = b.Length == 4 ? b[0] == 10 || b[0] == 172 && b[1] is >= 16 and <= 31 || b[0] == 192 && b[1] == 168
            : b.Length == 16 && (b[0] & 0xfe) == 0xfc && !expected.Host.Contains('%');
        return privateIp && Digest(expected.AnchorSha256) && Digest(expected.SpkiSha256) &&
            seen.Host == expected.Host && seen.Port == expected.Port && seen.Kind == expected.Kind &&
            seen.San == expected.Host && seen.AnchorSha256 == expected.AnchorSha256 && seen.SpkiSha256 == expected.SpkiSha256 &&
            seen.PathValid && seen.ServerEku && seen.NotBefore <= now.UtcSeconds && now.UtcSeconds < seen.NotAfter;
    }
}
public enum PairState { Closed, Open, Claimed, Issuing, Consumed, Uncertain }
public interface IEnrollmentMetadataStore { bool CommitSyntheticEnrollment(string flow); }
public sealed class PairingLifecycle
{
    private readonly object gate = new();
    private readonly ClockGuard clock = new();
    private readonly long expiresUtc, expiresTick;
    private string? flow;
    public PairState State { get; private set; }
    public PairingLifecycle(bool localConsent, ClockSample start, long expiresUtc, long expiresTick)
    {
        this.expiresUtc = expiresUtc; this.expiresTick = expiresTick;
        State = localConsent && clock.Observe(start) && expiresUtc > start.UtcSeconds && expiresTick > start.MonotonicTicks ? PairState.Open : PairState.Closed;
    }
    public bool Claim(string candidate, bool syntheticSecretMatches, ClockSample now)
    {
        lock (gate) {
            if (State != PairState.Open) return false;
            if (!clock.Observe(now) || now.UtcSeconds >= expiresUtc || now.MonotonicTicks >= expiresTick) { State = PairState.Closed; return false; }
            if (string.IsNullOrWhiteSpace(candidate) || !syntheticSecretMatches) { State = PairState.Closed; return false; }
            flow = candidate; State = PairState.Claimed; return true;
        }
    }
    public bool Confirm(string candidate, bool localOwnerConfirmed, ClockSample now, IEnrollmentMetadataStore store)
    {
        lock (gate) {
            if (State != PairState.Claimed) return false;
            if (flow != candidate || !localOwnerConfirmed || !clock.Observe(now) ||
                now.UtcSeconds >= expiresUtc || now.MonotonicTicks >= expiresTick) { State = PairState.Closed; return false; }
            State = PairState.Issuing;
            try {
                var committed = store.CommitSyntheticEnrollment(candidate);
                State = committed && State == PairState.Issuing ? PairState.Consumed : PairState.Uncertain;
            }
            catch { State = PairState.Uncertain; }
            return State == PairState.Consumed;
        }
    }
    public void CrashOrLostResponse() { lock (gate) State = PairState.Uncertain; }
}
public interface IRevocationMetadataStore { bool CommitSyntheticRevocation(); }
public sealed class CredentialPolicy(long issuedUtc, long expiresUtc, long expiresTick, string scope)
{
    private readonly object gate = new();
    private readonly ClockGuard clock = new();
    private bool denied;
    public bool Allows(bool syntheticVerifierMatches, string requestedScope, ClockSample now)
    {
        lock (gate) {
            if (!clock.Observe(now)) denied = true;
            return !denied && syntheticVerifierMatches && scope == "snapshot.read" && requestedScope == scope &&
                issuedUtc <= now.UtcSeconds && expiresUtc > issuedUtc && now.UtcSeconds < expiresUtc && now.MonotonicTicks < expiresTick;
        }
    }
    public bool Revoke(IRevocationMetadataStore store)
    {
        lock (gate) {
            denied = true; // failure still denies permanently in this instance
            try { return store.CommitSyntheticRevocation(); } catch { return false; }
        }
    }
}
public enum PublicationDecision { Rejected, NewSessionUnknownFreshness, AdvancedUnknownFreshness, Duplicate, Offline }
public sealed class PublicationTracker
{
    private readonly object gate = new();
    private string? session;
    private long revision = -1;
    private readonly HashSet<string> retired = [];
    private readonly int historyLimit;
    public PublicationTracker(int historyLimit) { if (historyLimit <= 0) throw new ArgumentOutOfRangeException(nameof(historyLimit)); this.historyLimit = historyLimit; }
    public PublicationDecision Observe(string candidate, long next, bool authenticated, bool synthetic, bool uncached)
    {
        lock (gate) {
            if (!authenticated || synthetic || !uncached || string.IsNullOrWhiteSpace(candidate) || candidate.Length > 128 || next < 0)
                return PublicationDecision.Rejected;
            if (candidate != session) {
                if (retired.Contains(candidate) || retired.Count >= historyLimit) return PublicationDecision.Rejected;
                if (session is not null) retired.Add(session);
                session = candidate; revision = next;
                return PublicationDecision.NewSessionUnknownFreshness;
            }
            if (next < revision) return PublicationDecision.Rejected;
            if (next == revision) return PublicationDecision.Duplicate;
            revision = next; return PublicationDecision.AdvancedUnknownFreshness;
        }
    }
    // Retain high-water marks across disconnect; reconnection must not resurrect an old publication.
    public PublicationDecision Disconnect() => PublicationDecision.Offline;
}
public interface ISyntheticSnapshotTransport { byte[] ReadSyntheticPayload(); }
