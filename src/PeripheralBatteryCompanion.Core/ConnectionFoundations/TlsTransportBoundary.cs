namespace PeripheralBatteryCompanion.Core.ConnectionFoundations;

// Intentionally uninhabited by public application code. No factory/issuer exists in D3.3A.
// A future platform adapter needs review before it can mint authenticated peer evidence.
public sealed class AuthenticatedTlsPeer
{
    private AuthenticatedTlsPeer() { }
}
public interface IIdentityMaterialSource { AuthenticatedTlsPeer? ObserveAuthenticatedIdentity(); }
public interface ITlsEvidenceValidator { TransportDecision Validate(AuthenticatedTlsPeer? evidence); }
public interface IEndpointPolicy { bool AcceptSynthetic(EndpointExpectation endpoint, SyntheticTrust observation, ClockSample time); }
public interface IReadOnlySnapshotSource { byte[] ReadAlreadyPublishedSnapshot(); }
public interface IAuthenticationDecision { TransportDecision AuthorizeSnapshotRead(AuthenticatedTlsPeer? peer); }
public interface ITransportLifecycle { TransportState State { get; } void Stop(); void Shutdown(); }
public enum TransportDecision { NativeProofRequired, Denied }
public sealed class DisabledTlsBoundary : ITlsEvidenceValidator, IAuthenticationDecision
{
    public TransportDecision Validate(AuthenticatedTlsPeer? evidence) => TransportDecision.NativeProofRequired;
    public TransportDecision AuthorizeSnapshotRead(AuthenticatedTlsPeer? peer) => TransportDecision.Denied;
    public byte[]? RetrieveSnapshot(AuthenticatedTlsPeer? peer, IReadOnlySnapshotSource source) => null;
}
public sealed record TransportPolicyData(int Connections, int RequestBytes, int HeaderBytes, int BodyBytes,
    long HandshakeTicks, long HeaderTicks, long RequestTicks, long ShutdownTicks, bool CancelOnStop)
{
    public bool Valid => Connections > 0 && RequestBytes > 0 && HeaderBytes > 0 && BodyBytes >= 0 &&
        (long)HeaderBytes + BodyBytes <= RequestBytes && HandshakeTicks > 0 && HeaderTicks > 0 &&
        RequestTicks > 0 && ShutdownTicks > 0 && CancelOnStop;
}
public enum TransportState { Off, Configured, Eligible, Stopped, Shutdown }
public sealed class InertTransportLifecycle : ITransportLifecycle
{
    private readonly object gate = new();
    public TransportState State { get; private set; }
    public bool ConfigureSynthetic(bool localConsent, TransportPolicyData policy)
    {
        lock (gate) {
            if (State != TransportState.Off) return false;
            State = localConsent && policy.Valid ? TransportState.Configured : TransportState.Stopped;
            return State == TransportState.Configured;
        }
    }
    public bool EvaluateSynthetic(bool metadataValid)
    {
        lock (gate) {
            if (State is not (TransportState.Configured or TransportState.Eligible)) return false;
            State = metadataValid ? TransportState.Eligible : TransportState.Stopped;
            return State == TransportState.Eligible; // no bind/connect/start method exists
        }
    }
    public void Restart() { lock (gate) if (State != TransportState.Shutdown) State = TransportState.Off; }
    public void Stop() { lock (gate) if (State != TransportState.Shutdown) State = TransportState.Stopped; }
    public void Shutdown() { lock (gate) State = TransportState.Shutdown; }
}
public readonly record struct EpochSample(string Boot, ClockSample Time);
public sealed class EpochClock
{
    private readonly string boot;
    private readonly ClockGuard clock = new();
    private bool failed;
    public EpochClock(EpochSample start) { boot = start.Boot; failed = string.IsNullOrWhiteSpace(boot) || !clock.Observe(start.Time); }
    public bool Observe(EpochSample now)
    {
        failed |= now.Boot != boot || !clock.Observe(now.Time);
        return !failed;
    }
}
public sealed class SyntheticTlsSession(EndpointExpectation expected, EpochSample start)
{
    private readonly object gate = new();
    private readonly EpochClock clock = new(start);
    private bool blocked;
    public bool Evaluate(SyntheticTrust observation, EpochSample now, bool trustRevoked, IReadOnlyList<string> syntheticChain)
    {
        lock (gate) {
            var chain = syntheticChain.ToArray();
            var chainMetadata = chain.Length > 0 && chain.All(x => x.Length == 64 && x.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f')) &&
                chain[^1] == expected.AnchorSha256;
            blocked |= trustRevoked || !chainMetadata || !clock.Observe(now) || !TrustMetadataPolicy.Decide(expected,observation,now.Time);
            return !blocked; // only synthetic metadata approval; cannot produce AuthenticatedTlsPeer
        }
    }
}
