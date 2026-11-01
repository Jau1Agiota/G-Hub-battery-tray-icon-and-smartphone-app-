namespace PeripheralBatteryCompanion.Core.TransportSecurity;

public enum AuthorizationLoadOutcome { Missing, VerifiedEmpty, VerifiedRecord, Uncertain }
public enum AuthorizationCommitOutcome { NotCommitted, Committed, Uncertain }
public sealed record AuthorizationLoad(AuthorizationLoadOutcome Outcome, AuthorizationRecord? Record);

/// <summary>
/// Trusted future protected persistence boundary. VerifiedEmpty is allowed only after
/// owner-reviewed fresh initialization, never for a missing existing-trust record.
/// Commit must atomically compare the generation and durably bind record + tombstone.
/// This interface and fake implementations are NOT proof of those native properties.
/// </summary>
public interface IAuthorizationJournal
{
    AuthorizationLoad Read();
    AuthorizationCommitOutcome Commit(long expectedGeneration, AuthorizationRecord next);
}

/// <summary>Non-listening lifecycle implementation, unwired from production. It does not authenticate TLS or bearer input.</summary>
public sealed class EnrollmentAuthorization
{
    private readonly object _gate = new();
    private readonly EnrollmentIdentity _identity;
    private readonly IAuthorizationJournal _journal;
    private readonly TimeProvider _clock;
    private readonly long _frequency;
    private DateTimeOffset _lastUtc;
    private long _lastTick;
    private AuthorizationRecord? _record;
    private bool _recovery, _exposure;
    private int _revocationRequested;
    private long _fence;
    private Guid? _window;
    private DateTimeOffset _windowExpiry;
    private long _windowTick;
    private EnrollmentClaim? _claim;
    private long _credentialTick;
    private TimeSpan _credentialBudget;

    public EnrollmentAuthorization(EnrollmentIdentity identity, IAuthorizationJournal journal, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(identity); ArgumentNullException.ThrowIfNull(journal);
        _identity = identity; _journal = journal; _clock = clock ?? TimeProvider.System;
        try
        {
            _lastUtc = _clock.GetUtcNow(); _lastTick = _clock.GetTimestamp();
            _frequency = _clock.TimestampFrequency;
            if (_lastUtc.Offset != TimeSpan.Zero || _frequency <= 0) { _recovery = true; return; }
            var loaded = journal.Read();
            if (loaded is null || (loaded.Outcome == AuthorizationLoadOutcome.VerifiedEmpty && loaded.Record is not null) ||
                (loaded.Outcome != AuthorizationLoadOutcome.VerifiedEmpty && loaded.Outcome != AuthorizationLoadOutcome.VerifiedRecord))
            { _recovery = true; return; }
            if (loaded.Outcome == AuthorizationLoadOutcome.VerifiedRecord)
            {
                if (loaded.Record is null || !identity.Matches(loaded.Record.Identity) || loaded.Record.IssuedAt > _lastUtc)
                { _recovery = true; return; }
                _record = loaded.Record;
                _credentialTick = _lastTick; _credentialBudget = _record.ExpiresAt - _lastUtc;
            }
            // No restored record enables session exposure. Check for clock changes during Read.
            _ = CheckClock(out _);
        }
        catch (Exception error) when (Ordinary(error)) { _recovery = true; }
    }

    public bool RecoveryRequired { get { lock (_gate) return _recovery; } }
    private long Generation => _record?.Generation ?? 0;

    public bool BeginEnrollmentByLocalOwner(Guid enrollmentId)
    {
        lock (_gate)
        {
            if (!CheckClock(out var now) || enrollmentId == Guid.Empty || _window is not null ||
                _record is { Revoked: false } || _record?.EnrollmentId == enrollmentId ||
                now < _identity.LeafNotBefore || now >= _identity.LeafNotAfter)
                return false;
            _exposure = false;
            _window = enrollmentId;
            _windowExpiry = _identity.LeafNotAfter - now < TimeSpan.FromSeconds(120) ? _identity.LeafNotAfter : now.AddSeconds(120);
            _windowTick = _lastTick;
            _claim = null;
            // A successfully revoked record permits a new explicit owner enrollment.
            // This never clears a recovery latch or restores previous authorization.
            Volatile.Write(ref _revocationRequested, 0);
            return true;
        }
    }

    public bool TryClaim(EnrollmentClaim claim)
    {
        ArgumentNullException.ThrowIfNull(claim);
        lock (_gate)
        {
            if (!WindowValid(out _) || Volatile.Read(ref _revocationRequested) != 0 ||
                _claim is not null || claim.EnrollmentId != _window) return false;
            _claim = claim;
            return true;
        }
    }

    public bool ConfirmEnrollmentByLocalOwner(string fullTranscriptSha256)
    {
        lock (_gate)
        {
            if (!WindowValid(out var now) || _claim is null || Volatile.Read(ref _revocationRequested) != 0) return false;
            if (fullTranscriptSha256 != _claim.TranscriptSha256(_identity)) { CloseWindow(); return false; }
            var issued = DateTimeOffset.FromUnixTimeSeconds(now.ToUnixTimeSeconds());
            var expiry = _identity.LeafNotAfter - issued <= TimeSpan.FromDays(90) ? _identity.LeafNotAfter : issued.AddDays(90);
            // Reserve a generation for retirement; never issue an unrevocable MaxValue generation.
            if (expiry <= now || Generation >= long.MaxValue - 1) return Fail();
            var candidate = new AuthorizationRecord(_identity, _claim.EnrollmentId, _claim.PhoneId,
                _claim.TokenSha256, fullTranscriptSha256, Generation + 1, issued, expiry, false);
            if (!Commit(candidate)) return false;
            // Persistence may have blocked past the claim window. Do not expose it;
            // retire the committed generation once, rather than retrying issuance.
            if (!WindowValid(out now) || now >= expiry || Volatile.Read(ref _revocationRequested) != 0)
            {
                CloseWindow();
                if (Generation < long.MaxValue) _ = Commit(candidate.Tombstone());
                return false;
            }
            _credentialTick = _lastTick; _credentialBudget = expiry - now;
            CloseWindow();
            return true;
        }
    }

    // Trusted LOCAL exposure decision only. Not a network route or persisted consent.
    public bool ApproveExposureByLocalOwner()
    {
        lock (_gate) { return _exposure = Current(); }
    }

    public bool TryPrepareAuthenticatedResponse(Guid phoneId, long generation, out ResponseLease? lease)
    {
        lease = null;
        if (Volatile.Read(ref _revocationRequested) != 0) return false;
        lock (_gate)
        {
            if (!_exposure || !Current() || _record!.PhoneId != phoneId || Generation != generation) return false;
            lease = new ResponseLease(this, Interlocked.Read(ref _fence), phoneId, generation);
            return true;
        }
    }

    // Recheck after response construction, immediately before a future transport write.
    // No lock is held across network I/O, and this does not recall already sent bytes.
    public bool RecheckBeforeRelease(ResponseLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (Volatile.Read(ref _revocationRequested) != 0 || lease.Fence != Interlocked.Read(ref _fence)) return false;
        lock (_gate) return ReferenceEquals(lease.Owner, this) && lease.Fence == Interlocked.Read(ref _fence) &&
            _exposure && Current() && _record!.PhoneId == lease.PhoneId && Generation == lease.Generation;
    }

    public bool RevokeByLocalOwner()
    {
        // Denial and lease invalidation precede waiting for the journal/lock. A hung
        // persistence call cannot keep a previously prepared lease eligible.
        Interlocked.Exchange(ref _revocationRequested, 1);
        Interlocked.Increment(ref _fence);
        lock (_gate)
        {
            _exposure = false; CloseWindow();
            if (_recovery || Interlocked.Read(ref _fence) < 0 || !CheckClock(out _) ||
                _record is null || Generation == long.MaxValue) return Fail();
            if (_record.Revoked) return true; // Already durably retired in this verified journal.
            return Commit(_record.Tombstone());
        }
    }

    public void DisableExposureByLocalOwner()
    {
        Interlocked.Exchange(ref _revocationRequested, 1);
        Interlocked.Increment(ref _fence);
        lock (_gate) { _exposure = false; CloseWindow(); if (Interlocked.Read(ref _fence) < 0) _ = Fail(); }
    }

    private bool Current()
    {
        if (Volatile.Read(ref _revocationRequested) != 0 || !CheckClock(out var now) ||
            _record is not { Revoked: false } || now < _record.IssuedAt || now >= _record.ExpiresAt ||
            !BeforeDeadline(_credentialTick, _credentialBudget))
        { _exposure = false; return false; }
        return true;
    }

    private bool WindowValid(out DateTimeOffset now)
    {
        if (!CheckClock(out now) || _window is null || now >= _windowExpiry ||
            !BeforeDeadline(_windowTick, TimeSpan.FromSeconds(120)))
        { CloseWindow(); return false; }
        return true;
    }

    private bool CheckClock(out DateTimeOffset now)
    {
        now = default;
        if (_recovery) return false;
        try
        {
            now = _clock.GetUtcNow(); long tick = _clock.GetTimestamp();
            if (now.Offset != TimeSpan.Zero || now < _lastUtc || tick < _lastTick || _clock.TimestampFrequency != _frequency)
                return Fail();
            _ = checked(tick - _lastTick);
            _lastUtc = now; _lastTick = tick;
            return true;
        }
        catch (Exception error) when (Ordinary(error)) { return Fail(); }
    }

    private bool BeforeDeadline(long start, TimeSpan budget)
    {
        try { return (decimal)checked(_lastTick - start) / _frequency < (decimal)budget.TotalSeconds; }
        catch (Exception error) when (Ordinary(error)) { return Fail(); }
    }

    private bool Commit(AuthorizationRecord next)
    {
        AuthorizationCommitOutcome outcome;
        try { outcome = _journal.Commit(Generation, next); }
        catch (Exception error) when (Ordinary(error)) { return Fail(); }
        if (outcome != AuthorizationCommitOutcome.Committed) return Fail();
        _record = next;
        return true;
    }

    private void CloseWindow() { _window = null; _claim = null; }
    private bool Fail() { _recovery = true; _exposure = false; CloseWindow(); return false; }
    private static bool Ordinary(Exception error) => error is not OutOfMemoryException and not StackOverflowException;
    public override string ToString() => "EnrollmentAuthorization (redacted; native activation unavailable)";

    public sealed class ResponseLease
    {
        internal EnrollmentAuthorization Owner { get; }
        internal long Fence { get; }
        internal Guid PhoneId { get; }
        internal long Generation { get; }
        internal ResponseLease(EnrollmentAuthorization owner, long fence, Guid phoneId, long generation)
        { Owner = owner; Fence = fence; PhoneId = phoneId; Generation = generation; }
        public override string ToString() => "ResponseLease (redacted; not authentication evidence)";
    }
}
