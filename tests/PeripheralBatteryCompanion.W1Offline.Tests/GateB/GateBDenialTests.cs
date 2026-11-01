using System;
using Xunit;

namespace PeripheralBatteryCompanion.W1Offline.Tests;

// No environment mutation, token, native lifecycle or digest invocation.
public sealed class GateBDenialTests : IDisposable
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1793394300);
    public GateBDenialTests() => GateEAuthorizationGate.ResetLatchForTesting();
    public void Dispose() => GateEAuthorizationGate.ResetLatchForTesting();

    private static W1PreflightPlan Plan() => new(
        "192.168.1.100", "CN=GateETestRoot", "01A2B3C4D5",
        Now.AddMinutes(-5), Now.AddMinutes(5), true, 15,
        false, false, true, true, 4096, 32, 4096,
        @"D:\Projects\W1-Gate-B\artifacts\report.txt");

    [Fact]
    public void MissingPlanAndPayload_Deny() => AssertDenied(null);

    [Fact]
    public void ExpiredPlan_Denies() => AssertDenied(Plan() with { NotAfterUtc = Now.AddSeconds(-1) });

    [Theory]
    [InlineData("valid")]
    [InlineData("future")]
    [InlineData("malformed")]
    public void PlanCannotAuthorize(string kind)
    {
        var plan = Plan();
        plan = kind switch
        {
            "future" => plan with { NotBeforeUtc = Now.AddMinutes(1) },
            "malformed" => plan with { CanonicalTestIp = null, NewReportDestination = null },
            _ => plan
        };
        AssertDenied(plan);
    }

    [Fact]
    public void RepeatedDenial_TripsLatch()
    {
        AssertDenied(Plan());
        var second = Assert.Throws<GateEExecutionBlockedException>(() =>
            GateEAuthorizationGate.EnsureAuthorized(Plan(), Now));
        Assert.Contains("one-shot latch", second.Message);
    }

    [Fact]
    public void NativeApprovalAndProvenanceHolds_Deny()
    {
        Assert.Throws<GateEExecutionBlockedException>(() => GateEAuthorizationGate.RequireExplicitNativeApproval());
        Assert.Throws<GateEExecutionBlockedException>(() => GateEAuthorizationGate.RequireVerifiedExecutionProvenance());
    }

    private static void AssertDenied(W1PreflightPlan? plan)
    {
        var denied = Assert.Throws<GateEExecutionBlockedException>(() =>
            GateEAuthorizationGate.EnsureAuthorized(plan, Now));
        Assert.Contains("authenticated permit validation", denied.Message);
    }
}
