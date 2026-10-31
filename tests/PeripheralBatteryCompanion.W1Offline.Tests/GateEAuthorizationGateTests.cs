using System;
using Xunit;

namespace PeripheralBatteryCompanion.W1Offline.Tests;

public sealed class GateEAuthorizationGateTests : IDisposable
{
    public GateEAuthorizationGateTests()
    {
        GateEAuthorizationGate.ResetLatchForTesting();
        Environment.SetEnvironmentVariable("PBC_GATE_E_AUTHORIZED", null);
    }

    public void Dispose()
    {
        GateEAuthorizationGate.ResetLatchForTesting();
        Environment.SetEnvironmentVariable("PBC_GATE_E_AUTHORIZED", null);
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
    public void EnsureAuthorized_Throws_WhenEnvironmentTokenMissing()
    {
        DateTimeOffset now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var plan = CreateTestPlan(now.AddMinutes(-5), now.AddMinutes(5));

        Environment.SetEnvironmentVariable("PBC_GATE_E_AUTHORIZED", null);

        var ex = Assert.Throws<GateEExecutionBlockedException>(() =>
            GateEAuthorizationGate.EnsureAuthorized(plan, now, "mock-permit"));
        Assert.Contains("Gate 2", ex.Message);
    }

    [Fact]
    public void EnsureAuthorized_Throws_WhenPermitPayloadEmpty()
    {
        DateTimeOffset now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var plan = CreateTestPlan(now.AddMinutes(-5), now.AddMinutes(5));

        Environment.SetEnvironmentVariable("PBC_GATE_E_AUTHORIZED", "1");

        var ex = Assert.Throws<GateEExecutionBlockedException>(() =>
            GateEAuthorizationGate.EnsureAuthorized(plan, now, ""));
        Assert.Contains("Gate 3", ex.Message);
    }

    [Fact]
    public void EnsureAuthorized_Throws_WhenOutsideTimeWindow()
    {
        DateTimeOffset now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var plan = CreateTestPlan(now.AddMinutes(10), now.AddMinutes(20));

        Environment.SetEnvironmentVariable("PBC_GATE_E_AUTHORIZED", "1");

        var ex = Assert.Throws<GateEExecutionBlockedException>(() =>
            GateEAuthorizationGate.EnsureAuthorized(plan, now, "valid-scoped-permit"));
        Assert.Contains("UTC window", ex.Message);
    }

    [Fact]
    public void EnsureAuthorized_EnforcesOneShotLatch_OnSecondCall()
    {
        DateTimeOffset now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var plan = CreateTestPlan(now.AddMinutes(-5), now.AddMinutes(5));

        Environment.SetEnvironmentVariable("PBC_GATE_E_AUTHORIZED", "1");

        GateEAuthorizationGate.EnsureAuthorized(plan, now, "valid-permit-once");

        var ex = Assert.Throws<GateEExecutionBlockedException>(() =>
            GateEAuthorizationGate.EnsureAuthorized(plan, now, "valid-permit-once"));
        Assert.Contains("Gate 4", ex.Message);
    }
}
