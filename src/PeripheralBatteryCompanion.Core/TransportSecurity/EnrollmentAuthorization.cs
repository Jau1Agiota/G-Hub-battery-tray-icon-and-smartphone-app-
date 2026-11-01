using System.Security.Cryptography;

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

/// <summary>Non-listening lifecycle and bearer-verifier core, unwired from production. It does not authenticate TLS.</summary>
public sealed class EnrollmentAuthorization : IDisposable
{
    private readonly object _gate = new();
    private readonly object _signals = new();
    private CancellationTokenSource _invalidation = new();
    private Task _invalidationTask = Task.CompletedTask;
    private readonly EnrollmentIdentity _identity;
    private readonly IAuthorizationJournal _journal;
    private readonly TimeProvider _clock;
    private readonly long _frequency;
    private DateTimeOffset _lastUtc;
    private long _lastTick;
    private AuthorizationRecord? _record;
    private bool _recovery, _exposure, _disposed;
    private int _revocationRequested;
    private long _fence;
    private Guid? _window;
    private DateTimeOffset _windowExpiry;
    private long _windowTick;
    private EnrollmentClaim? _claim;
    private long _credentialTick;
    private TimeSpan _credentialBudget;
    private bool _exposureStarted;
    private long _exposureTick;

    public EnrollmentAuthorization(EnrollmentIdentity identity, IAuthorizationJournal journal, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(identity); ArgumentNullException.ThrowIfNull(journal);
        _identity = identity; _journal = journal; _clock = clock ?? TimeProvider.System;
        try
        {
            _lastUtc = _clock.GetUtcNow(); _lastTick = _clock.GetTimestamp();
            _frequency = _clock.TimestampFrequency;
            if (_lastUtc.Offset != TimeSpan.Zero || _frequency <= 0) { _ = Fail(); return; }
            var loaded = journal.Read();
            if (loaded is null || (loaded.Outcome == AuthorizationLoadOutcome.VerifiedEmpty && loaded.Record is not null) ||
                (loaded.Outcome != AuthorizationLoadOutcome.VerifiedEmpty && loaded.Outcome != AuthorizationLoadOutcome.VerifiedRecord))
            { _ = Fail(); return; }
            if (loaded.Outcome == AuthorizationLoadOutcome.VerifiedRecord)
            {
                if (loaded.Record is null || !identity.Matches(loaded.Record.Identity) || loaded.Record.IssuedAt > _lastUtc)
                { _ = Fail(); return; }
                _record = loaded.Record;
                _credentialTick = _lastTick; _credentialBudget = _record.ExpiresAt - _lastUtc;
            }
            // No restored record enables session exposure. Check for clock changes during Read.
            _ = CheckClock(out _);
        }
        catch (Exception error) when (Ordinary(error)) { _ = Fail(); }
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
            _exposureStarted = false;
            RotateInvalidation();
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
        lock (_gate)
        {
            if (_exposureStarted) return ExposureCurrent();
            if (!Current()) return false;
            _exposureStarted = true; _exposureTick = _lastTick;
            return _exposure = true;
        }
    }

    internal bool TryReadApprovedEnrollment(EnrollmentClaim claim, out AuthorizationRecord? record)
    {
        lock (_gate)
        {
            record = Current() && _record!.EnrollmentId == claim.EnrollmentId && _record.PhoneId == claim.PhoneId &&
                _record.TokenSha256 == claim.TokenSha256 ? _record : null;
            return record is not null;
        }
    }
    internal void RejectPendingEnrollment() { lock (_gate) CloseWindow(); }
    internal void DenyForSecurityFailure() { lock (_gate) _ = Fail(); }
    public bool TryAuthenticateBearer(string? authorizationHeader, out ResponseLease? lease)
    {
        lease = null;
        if (Volatile.Read(ref _revocationRequested) != 0) return false;
        Span<byte> digest = stackalloc byte[32];
        try
        {
            if (!ProtocolCredentials.TryHashBearer(authorizationHeader, digest)) return false;
            lock (_gate)
            {
                if (!ExposureCurrent() || !CryptographicOperations.FixedTimeEquals(digest, Convert.FromHexString(_record!.TokenSha256))) return false;
                lease = new(this, Interlocked.Read(ref _fence), _record.PhoneId, Generation, _invalidation.Token);
                return true;
            }
        }
        finally { CryptographicOperations.ZeroMemory(digest); }
    }

    public bool TryPrepareAuthenticatedResponse(Guid phoneId, long generation, out ResponseLease? lease)
    {
        lease = null;
        if (Volatile.Read(ref _revocationRequested) != 0) return false;
        lock (_gate)
        {
            if (!ExposureCurrent() || _record!.PhoneId != phoneId || Generation != generation) return false;
            lease = new ResponseLease(this, Interlocked.Read(ref _fence), phoneId, generation, _invalidation.Token);
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
            ExposureCurrent() && _record!.PhoneId == lease.PhoneId && Generation == lease.Generation;
    }

    public bool RevokeByLocalOwner()
    {
        // Denial and lease invalidation precede waiting for the journal/lock. A hung
        // persistence call cannot keep a previously prepared lease eligible.
        Interlocked.Exchange(ref _revocationRequested, 1);
        Interlocked.Increment(ref _fence);
        SignalInvalidation();
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
        SignalInvalidation();
        lock (_gate) { _exposure = false; CloseWindow(); if (Interlocked.Read(ref _fence) < 0) _ = Fail(); }
    }

    private bool Current()
    {
        if (Volatile.Read(ref _revocationRequested) != 0 || !CheckClock(out var now) ||
            _record is not { Revoked: false })
        { _exposure = false; return false; }
        if (now < _record.IssuedAt || now >= _record.ExpiresAt || !BeforeDeadline(_credentialTick, _credentialBudget))
        { _exposure = false; Interlocked.Exchange(ref _revocationRequested, 1); SignalInvalidation(); return false; }
        try
        {
            var stored = _journal.Read();
            if (stored.Outcome != AuthorizationLoadOutcome.VerifiedRecord || stored.Record is null ||
                (!ReferenceEquals(stored.Record, _record) && !AuthorizationRecordCodec.Encode(stored.Record).AsSpan().SequenceEqual(AuthorizationRecordCodec.Encode(_record))))
                return Fail();
        }
        catch (Exception error) when (Ordinary(error)) { return Fail(); }
        if (Volatile.Read(ref _revocationRequested) != 0) { _exposure = false; return false; }
        return true;
    }
    private bool ExposureCurrent()
    {
        if (!_exposure || !Current()) return false;
        if (BeforeDeadline(_exposureTick, TimeSpan.FromHours(8))) return true;
        _exposure = false; Interlocked.Exchange(ref _revocationRequested, 1); SignalInvalidation(); return false;
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
        if (_recovery || _disposed) return false;
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
    private bool Fail()
    {
        bool first = !_recovery;
        _recovery = true; _exposure = false; CloseWindow();
        Interlocked.Exchange(ref _revocationRequested, 1);
        SignalInvalidation();
        if (first && _journal is IAuthorizationJournalSession session)
            try { session.MarkUncertain(); } catch (Exception error) when (Ordinary(error)) { }
        return false;
    }
    public void Dispose()
    {
        if (Volatile.Read(ref _disposed)) return;
        Interlocked.Exchange(ref _revocationRequested, 1); Interlocked.Increment(ref _fence);
        SignalInvalidation();
        lock (_gate)
        {
            if (_disposed) return;
            _exposure = false; CloseWindow(); _disposed = true;
            if (_journal is IAuthorizationJournalSession session)
            {
                try { if (_recovery) session.MarkUncertain(); session.Dispose(); }
                catch (Exception error) when (Ordinary(error)) { _recovery = true; }
            }
            lock (_signals)
            {
                var source = _invalidation;
                _ = _invalidationTask.ContinueWith(task => { if (task.IsFaulted) _ = task.Exception; source.Dispose(); },
                    CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
    }
    private void SignalInvalidation()
    {
        lock (_signals)
        {
            if (Volatile.Read(ref _disposed)) return;
            if (_invalidation.IsCancellationRequested) return;
            _invalidationTask = _invalidation.CancelAsync();
            _ = _invalidationTask.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }
    private void RotateInvalidation()
    {
        lock (_signals)
        {
            var old = _invalidation; var finished = _invalidationTask;
            _invalidation = new(); _invalidationTask = Task.CompletedTask;
            _ = finished.ContinueWith(task => { if (task.IsFaulted) _ = task.Exception; old.Dispose(); },
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }
    private static bool Ordinary(Exception error) => error is not OutOfMemoryException and not StackOverflowException;
    public override string ToString() => "EnrollmentAuthorization (redacted; native activation unavailable)";

    public sealed class ResponseLease
    {
        internal EnrollmentAuthorization Owner { get; }
        internal long Fence { get; }
        internal Guid PhoneId { get; }
        internal long Generation { get; }
        internal CancellationToken Stop { get; }
        internal ResponseLease(EnrollmentAuthorization owner, long fence, Guid phoneId, long generation, CancellationToken stop)
        { Owner = owner; Fence = fence; PhoneId = phoneId; Generation = generation; Stop = stop; }
        public override string ToString() => "ResponseLease (redacted; not authentication evidence)";
    }
}
