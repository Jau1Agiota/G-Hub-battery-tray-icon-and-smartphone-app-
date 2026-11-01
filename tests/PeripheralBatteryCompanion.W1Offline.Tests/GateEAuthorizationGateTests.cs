using System;
using Xunit;

namespace PeripheralBatteryCompanion.W1Offline.Tests;

public sealed class GateEAuthorizationGateTests : IDisposable
{
    // Fixed synthetic time; each xUnit case gets a fresh fixture and latch reset.
    // This is process-local hygiene, not process isolation or authorization.
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1793394300);
    public GateEAuthorizationGateTests()
    {
        GateEAuthorizationGate.ResetLatchForTesting();
    }

    public void Dispose()
    {
        GateEAuthorizationGate.ResetLatchForTesting();
    }

    private static W1PreflightPlan CreateTestPlan(DateTimeOffset notBefore, DateTimeOffset notAfter)
    {
        return new W1PreflightPlan(
            CanonicalTestIp: "192.168.1.100",
            SyntheticSubject: "CN=GateETestRoot",
            PublicSerialHex: "01A2B3C4D5",
            NotBeforeUtc: notBefore,
            NotAfterUtc: notAfter,
            ClockConfidenceConfirmed: true,
            ClockUncertaintySeconds: 15,
            SanCritical: false,
            EkuCritical: false,
            UsageCritical: true,
            BasicCritical: true,
            PublicBufferAcceptanceBytes: 4096,
            MetadataAcceptanceBytes: 32,
            ReportAcceptanceBytes: 4096,
            NewReportDestination: @"D:\Projects\W1-Gate-B\artifacts\report.txt");
    }

    [Fact]
    public void EnsureAuthorized_FailsClosed_EvenWhenLegacyEnvironmentTokenAndOpaquePayloadArePresent()
    {
        DateTimeOffset now = Now;
        var plan = CreateTestPlan(now.AddMinutes(-5), now.AddMinutes(5));
        string? previousToken = Environment.GetEnvironmentVariable("PBC_GATE_E_AUTHORIZED");

        try
        {
            Environment.SetEnvironmentVariable("PBC_GATE_E_AUTHORIZED", "1");

            var ex = Assert.Throws<GateEExecutionBlockedException>(() =>
                GateEAuthorizationGate.EnsureAuthorized(plan, now, "mock-permit"));

            Assert.Contains("authenticated permit validation", ex.Message);
            Assert.Contains("trusted atomic one-time nonce consumption", ex.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PBC_GATE_E_AUTHORIZED", previousToken);
        }
    }

    [Fact]
    public void EnsureAuthorized_FailsClosed_WhenPlanIsNullAndPermitIsAbsent()
    {
        DateTimeOffset now = Now;

        var ex = Assert.Throws<GateEExecutionBlockedException>(() =>
            GateEAuthorizationGate.EnsureAuthorized(null, now, null));

        Assert.Contains("authenticated permit validation", ex.Message);
    }

    [Fact]
    public void EnsureAuthorized_FailsClosed_WhenPlanIsOutsideItsValidityWindow()
    {
        DateTimeOffset now = Now;
        var expiredPlan = CreateTestPlan(now.AddMinutes(-20), now.AddMinutes(-10));

        var ex = Assert.Throws<GateEExecutionBlockedException>(() =>
            GateEAuthorizationGate.EnsureAuthorized(expiredPlan, now, "opaque-untrusted-payload"));

        Assert.Contains("authenticated permit validation", ex.Message);
        Assert.Contains("trusted atomic one-time nonce consumption", ex.Message);
    }

    [Fact]
    public void EnsureAuthorized_TripsProcessLocalLatch_OnFirstDeniedAttempt()
    {
        DateTimeOffset now = Now;
        var plan = CreateTestPlan(now.AddMinutes(-5), now.AddMinutes(5));

        var first = Assert.Throws<GateEExecutionBlockedException>(() =>
            GateEAuthorizationGate.EnsureAuthorized(plan, now, "mock-permit"));
        Assert.Contains("authenticated permit validation", first.Message);

        var second = Assert.Throws<GateEExecutionBlockedException>(() =>
            GateEAuthorizationGate.EnsureAuthorized(plan, now, "mock-permit"));
        Assert.Contains("one-shot latch", second.Message);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("0", "opaque")]
    [InlineData("1", "opaque")]
    [InlineData("true", "signed-looking-but-untrusted")]
    public void EnsureAuthorized_RejectsAllLegacyTokenAndPayloadCases(string? token, string? payload)
    {
        string? previousToken = Environment.GetEnvironmentVariable("PBC_GATE_E_AUTHORIZED");
        try
        {
            Environment.SetEnvironmentVariable("PBC_GATE_E_AUTHORIZED", token);
            var plan = CreateTestPlan(Now.AddMinutes(-5), Now.AddMinutes(5));
            var denied = Assert.Throws<GateEExecutionBlockedException>(() =>
                GateEAuthorizationGate.EnsureAuthorized(plan, Now, payload));
            Assert.Contains("authenticated permit validation", denied.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PBC_GATE_E_AUTHORIZED", previousToken);
        }
    }

    [Theory]
    [InlineData("future")]
    [InlineData("malformed")]
    [InlineData("missing")]
    public void EnsureAuthorized_RejectsInvalidPlansWithoutSerialization(string kind)
    {
        W1PreflightPlan? plan = CreateTestPlan(Now.AddMinutes(-5), Now.AddMinutes(5));
        plan = kind switch
        {
            "future" => plan with { NotBeforeUtc = Now.AddMinutes(1), NotAfterUtc = Now.AddMinutes(5) },
            "malformed" => plan with { CanonicalTestIp = null, NewReportDestination = null },
            _ => null
        };
        var denied = Assert.Throws<GateEExecutionBlockedException>(() =>
            GateEAuthorizationGate.EnsureAuthorized(plan, Now, "opaque"));
        Assert.Contains("authenticated permit validation", denied.Message);
    }

    [Fact]
    public void NativeApprovalAndProvenanceHolds_RemainUnconditionallyDenied()
    {
        Assert.Throws<GateEExecutionBlockedException>(() => GateEAuthorizationGate.RequireExplicitNativeApproval());
        Assert.Throws<GateEExecutionBlockedException>(() => GateEAuthorizationGate.RequireVerifiedExecutionProvenance());
        // Do not invoke W1NativeLifecycle, even with null inputs.
    }
}
