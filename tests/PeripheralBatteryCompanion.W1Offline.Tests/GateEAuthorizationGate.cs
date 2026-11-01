using System;
using System.Threading;

namespace PeripheralBatteryCompanion.W1Offline.Tests;

public sealed class GateEExecutionBlockedException : Exception
{
    public GateEExecutionBlockedException(string message) : base(message) { }
    public GateEExecutionBlockedException(string message, Exception innerException) : base(message, innerException) { }
}

internal static class GateEAuthorizationGate
{
    private static int _latchState = 0;

    internal static void RequireExplicitNativeApproval()
    {
        throw new GateEExecutionBlockedException("GATE E BLOCKED: native execution requires a separately reviewed and approved source revision.");
    }

    // Fail closed: executable, loaded runtime, test assembly, provider, and
    // source-revision provenance have not been independently authenticated.
    // This hold must not be removed without a separately reviewed verifier.
    internal static void RequireVerifiedExecutionProvenance()
    {
        throw new GateEExecutionBlockedException(
            "GATE E BLOCKED: executable and runtime provenance verification is not implemented.");
    }

    public static void ResetLatchForTesting()
    {
        Interlocked.Exchange(ref _latchState, 0);
    }

    public static void EnsureAuthorized(W1PreflightPlan? plan, DateTimeOffset now, string? permitPayload = null)
    {
        // The environment-variable token and an opaque non-empty string are not
        // authenticated consent, exact-operation authorization, or replay protection.
        // GateEPermit.TryAcquireFromFile currently fails closed because no trusted,
        // atomic, rollback-resistant nonce-consumption mechanism has been approved
        // and implemented. Keep this API fail-closed until that mechanism exists.
        _ = plan;
        _ = now;
        _ = permitPayload;

        if (Interlocked.CompareExchange(ref _latchState, 1, 0) != 0)
        {
            throw new GateEExecutionBlockedException(
                "GATE E BLOCKED: process-local one-shot latch has already been tripped.");
        }

        throw new GateEExecutionBlockedException(
            "GATE E BLOCKED: authenticated permit validation and trusted atomic one-time nonce consumption are not implemented. Environment variables and opaque permit strings cannot authorize execution.");
    }
}
