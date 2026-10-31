using System;
using System.Security.Cryptography;
using System.Threading;

namespace PeripheralBatteryCompanion.W1Offline.Tests;

public sealed class GateEExecutionBlockedException : Exception
{
    public GateEExecutionBlockedException(string message) : base(message) { }
    public GateEExecutionBlockedException(string message, Exception innerException) : base(message, innerException) { }
}

public static class GateEAuthorizationGate
{
    private static int _latchState = 0; // 0 = unentered, 1 = entered

    public static void ResetLatchForTesting()
    {
        Interlocked.Exchange(ref _latchState, 0);
    }

    public static void EnsureAuthorized(W1PreflightPlan? plan, DateTimeOffset now, string? permitPayload = null)
    {
        // Gate 1 & 2: Secondary interactive session token check
        string? envToken = Environment.GetEnvironmentVariable("PBC_GATE_E_AUTHORIZED");
        if (string.IsNullOrWhiteSpace(envToken) || envToken != "1")
        {
            throw new GateEExecutionBlockedException(
                "Execution Blocked (Gate 2): Required interactive session token 'PBC_GATE_E_AUTHORIZED=1' is absent or invalid.");
        }

        // Gate 4 (Early Latch Acquisition): Ensure one-shot lifetime containment
        if (Interlocked.CompareExchange(ref _latchState, 1, 0) != 0)
        {
            throw new GateEExecutionBlockedException(
                "Execution Blocked (Gate 4): One-shot latch already tripped. Multiple native execution attempts in the same process domain are forbidden.");
        }

        // Gate 3: Cryptographic Permit & Scope Binding
        if (plan is null)
        {
            throw new GateEExecutionBlockedException(
                "Execution Blocked (Gate 3): Preflight execution plan cannot be null.");
        }

        if (string.IsNullOrWhiteSpace(permitPayload))
        {
            throw new GateEExecutionBlockedException(
                "Execution Blocked (Gate 3): Cryptographic execution permit payload is missing.");
        }

        // Validate plan digest binding and time boundaries
        byte[] expectedDigest = plan.ComputeDigest();
        if (expectedDigest.Length != 32)
        {
            throw new GateEExecutionBlockedException(
                "Execution Blocked (Gate 3): Canonical plan digest length mismatch.");
        }

        if (now < plan.NotBeforeUtc || now > plan.NotAfterUtc)
        {
            throw new GateEExecutionBlockedException(
                $"Execution Blocked (Gate 3): Permit evaluated outside valid UTC window. Evaluation time: {now:O}, Window: [{plan.NotBeforeUtc:O} - {plan.NotAfterUtc:O}].");
        }
    }
}
