using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace PeripheralBatteryCompanion.W1Offline.Tests;

public sealed class W1OfflineLifecycleTests
{
    [Fact]
    public void SingleShot_UnnamedNonExportableP256_PublicLeaf_NoStore()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        // Construct canonical plan targeting Gate E report path
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

        // Attempt permit acquisition; if absent or invalid, execute unpermitted baseline
        if (!GateEPermit.TryAcquireFromFile(plan, now, out var permit) || permit is null)
        {
            var result = W1NativeLifecycle.Execute(null, null, now);
            Assert.Equal("NOT_RUN", result.Outcome);
            Assert.False(result.NativeEntered);
            return;
        }

        // Authorized permit acquired: execute single-shot Gate E test
        var execResult = W1NativeLifecycle.Execute(plan, permit, now);
        Assert.True(execResult.NativeEntered);
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

        // Assert exact match against the verified frozen test vector
        Assert.Equal("3B6546314CDEB4177804D45E1795882205A2E2FF9941C759E1F014941FC98CCA", digestHex);
    }
}