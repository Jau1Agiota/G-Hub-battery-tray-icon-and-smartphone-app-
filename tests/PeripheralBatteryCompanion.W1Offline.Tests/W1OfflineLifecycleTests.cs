using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace PeripheralBatteryCompanion.W1Offline.Tests;

public sealed class W1OfflineLifecycleTests
{
    [Fact]
    [Trait("Category", "GateE_Native_Protected")]
    [Trait("ExecutionMode", "ManualHardwareHold")]
    public void SingleShot_UnnamedNonExportableP256_PublicLeaf_NoStore()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var plan = new W1PreflightPlan(
            CanonicalTestIp: "192.168.1.100",
            SyntheticSubject: "CN=GateETestRoot",
            PublicSerialHex: "01A2B3C4D5",
            NotBeforeUtc: new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, TimeSpan.Zero),
            NotAfterUtc: new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 10, 0, TimeSpan.Zero),
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

        // Fail-Closed Gate check: ensures execution is actively blocked unless all conjunctive gates are met
        if (!GateEPermit.TryAcquireFromFile(plan, now, out var permit) || permit is null)
        {
            var result = W1NativeLifecycle.Execute(null, null, now);
            Assert.Equal("NOT_RUN", result.Outcome);
            Assert.False(result.NativeEntered);
            return;
        }

        // Latch and environment containment validation prior to invoking native entry point
        GateEAuthorizationGate.EnsureAuthorized(plan, now, permit.ToString());

        var execResult = W1NativeLifecycle.Execute(plan, permit, now);
        Assert.True(execResult.NativeEntered);
        Assert.True(execResult.PublicLeafObserved);
        Assert.Equal("LIMITED_OBSERVATIONS_CLEANUP_UNCONFIRMED", execResult.Outcome);
        Assert.Null(execResult.FailurePhase);
        Assert.Null(execResult.HResult);
        Assert.Empty(execResult.SecondaryFailures);
        Assert.Equal(8, execResult.Metadata.Count);
        Assert.All(execResult.Metadata, obs => Assert.True(W1MetadataClassifier.MayContinue(obs.Field, obs.Status)));
        Assert.Equal(3, execResult.Cleanup.Count);
        Assert.All(execResult.Cleanup, c => { Assert.True(c.Attempted); Assert.True(c.DisposeReturned); });
        Assert.True(execResult.ReportWriteReturned);
    }

    [Fact]
    public void FrozenTestVector_CanonicalSerializationAndDigest_MatchesSpecification()
    {
        var plan = new W1PreflightPlan(
            CanonicalTestIp: "192.168.1.100",
            SyntheticSubject: "CN=GateETestRoot",
            PublicSerialHex: "01A2B3C4D5",
            NotBeforeUtc: DateTimeOffset.FromUnixTimeSeconds(1793394000),
            NotAfterUtc: DateTimeOffset.FromUnixTimeSeconds(1793394600),
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

        byte[] digest = plan.ComputeDigest();
        string digestHex = Convert.ToHexString(digest);

        Assert.Equal("3B6546314CDEB4177804D45E1795882205A2E2FF9941C759E1F014941FC98CCA", digestHex);
    }
}
