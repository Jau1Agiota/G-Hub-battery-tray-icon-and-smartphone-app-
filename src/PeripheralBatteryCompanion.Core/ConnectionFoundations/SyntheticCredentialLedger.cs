namespace PeripheralBatteryCompanion.Core.ConnectionFoundations;

public enum LedgerState { RestartLocked, Ready, Uncertain, Closed }
public enum CommitDisposition { CommittedAndFlushed, NotCommitted, Uncertain }
public sealed record CommitAcknowledgment(CommitDisposition Disposition, long Version);
public sealed record SyntheticCredentialEntry(string Phone, string CredentialLabel, long Generation,
    string Scope, long IssuedUtc, long ExpiresUtc, long ExpiresTick, bool Revoked);
/// <summary>Future atomic compare-version commit + durable flush contract. NO concrete storage adapter.
/// Labels are synthetic identifiers, never bearer secrets or token verifiers.
/// Committed means complete durable record, rollback/conflict/corruption must never return Committed.</summary>
public interface IAtomicSyntheticLedger
{
    CommitAcknowledgment CommitAndFlush(long expectedVersion, SyntheticCredentialEntry replacement);
}
public sealed class SyntheticCredentialLedger
{
    private readonly object gate = new();
    private readonly IAtomicSyntheticLedger store;
    private readonly EpochClock clock;
    private readonly int capacity;
    private readonly Dictionary<string,SyntheticCredentialEntry> entries = [];
    private readonly HashSet<string> usedLabels = [];
    private long version;
    public LedgerState State { get; private set; }
    public SyntheticCredentialLedger(IAtomicSyntheticLedger store, EpochSample start, int capacity, bool newEmptySyntheticSession)
    {
        this.store = store; clock = new(start); this.capacity = capacity;
        // NO restore/unlock API. An old/corrupt/partial/rolled-back image cannot restore authorization.
        State = newEmptySyntheticSession && capacity > 0 && clock.Observe(start) ? LedgerState.Ready : LedgerState.RestartLocked;
    }
    private bool Ready(EpochSample now)
    {
        if (State != LedgerState.Ready) return false;
        if (!clock.Observe(now)) { State = LedgerState.Uncertain; return false; }
        return true;
    }
    private static bool Label(string s) => s.Length is > 0 and <= 128 && s.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');
    private bool Commit(SyntheticCredentialEntry entry)
    {
        if (version == long.MaxValue) { State = LedgerState.Uncertain; return false; }
        var expected = version;
        // Deny reads/reentrant actions while the callback may have side effects.
        State = LedgerState.Uncertain;
        try {
            var ack = store.CommitAndFlush(expected,entry);
            if (State != LedgerState.Uncertain || ack.Disposition != CommitDisposition.CommittedAndFlushed ||
                ack.Version != expected + 1) return false;
            entries[entry.Phone] = entry; usedLabels.Add(entry.CredentialLabel); version = ack.Version;
            State = LedgerState.Ready; return true;
        } catch { return false; }
    }
    public bool IssueSynthetic(string phone, string label, long expiresUtc, long expiresTick, EpochSample now)
    {
        lock (gate) {
            if (!Ready(now) || !Label(phone) || !Label(label) || entries.ContainsKey(phone) || usedLabels.Contains(label) ||
                usedLabels.Count >= capacity || expiresUtc <= now.Time.UtcSeconds || expiresTick <= now.Time.MonotonicTicks) return false;
            return Commit(new(phone,label,1,"snapshot.read",now.Time.UtcSeconds,expiresUtc,expiresTick,false));
        }
    }
    public bool RotateSynthetic(string phone, string label, long expiresUtc, long expiresTick, EpochSample now)
    {
        lock (gate) {
            if (!Ready(now) || !Label(label) || !entries.TryGetValue(phone,out var old) || old.Revoked ||
                old.Generation == long.MaxValue || usedLabels.Contains(label) || usedLabels.Count >= capacity ||
                expiresUtc <= now.Time.UtcSeconds || expiresTick <= now.Time.MonotonicTicks) return false;
            return Commit(new(phone,label,old.Generation+1,"snapshot.read",now.Time.UtcSeconds,expiresUtc,expiresTick,false));
        }
    }
    public bool RevokeSynthetic(string phone, EpochSample now)
    {
        lock (gate) {
            if (!Ready(now) || !entries.TryGetValue(phone,out var entry)) return false;
            if (entry.Revoked) return true;
            entries[phone] = entry with { Revoked = true }; // local deny precedes persistence
            return Commit(entries[phone]);
        }
    }
    public bool ReadSyntheticDecision(string phone, string label, string scope, EpochSample now)
    {
        lock (gate) return Ready(now) && entries.TryGetValue(phone,out var e) && !e.Revoked && e.CredentialLabel == label &&
            scope == "snapshot.read" && e.Scope == scope && e.IssuedUtc <= now.Time.UtcSeconds &&
            now.Time.UtcSeconds < e.ExpiresUtc && now.Time.MonotonicTicks < e.ExpiresTick;
    }
    public void CrashOrLostAcknowledgment() { lock (gate) State = LedgerState.Closed; }
}
