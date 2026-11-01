using System;
using Xunit;

namespace PeripheralBatteryCompanion.W1Offline.Tests;

// Byte-only/preflight coverage. Never call ComputeDigest or the native lifecycle here.
public sealed class W1PreflightPlanTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1793394300);

    private static W1PreflightPlan CreatePlan() => new(
        CanonicalTestIp: "192.168.1.100",
        SyntheticSubject: "CN=GateETestRoot",
        PublicSerialHex: "01A2B3C4D5",
        NotBeforeUtc: DateTimeOffset.FromUnixTimeSeconds(1793394000),
        NotAfterUtc: DateTimeOffset.FromUnixTimeSeconds(1793394600),
        ClockConfidenceConfirmed: true,
        ClockUncertaintySeconds: 15,
        SanCritical: false, EkuCritical: false, UsageCritical: true, BasicCritical: true,
        PublicBufferAcceptanceBytes: 4096, MetadataAcceptanceBytes: 32,
        ReportAcceptanceBytes: 4096,
        NewReportDestination: @"D:\Projects\W1-Gate-B\artifacts\report.txt");

    [Fact]
    public void PermitOperations_AreInert_ForValidInvalidAndMissingPlans()
    {
        var valid = CreatePlan();
        Assert.True(valid.InputsValid(Now));
        // A constructible inert object is not authority; even it can never match.
        var inert = new GateEPermit();
        W1PreflightPlan?[] plans =
        [
            valid,
            valid with { CanonicalTestIp = null, NewReportDestination = null },
            valid with { NotAfterUtc = Now.AddMinutes(-1) },
            null
        ];
        foreach (var plan in plans)
        {
            // Deliberate runtime-null robustness check of legacy nonnullable APIs.
            Assert.False(GateEPermit.TryAcquireFromFile(plan!, Now, out var permit));
            Assert.Null(permit);
            Assert.False(inert.Matches(plan!));
            Assert.False(W1PreflightPlan.TryConsume(plan, inert, Now));
            Assert.False(W1PreflightPlan.TryConsume(plan, null, Now));
            Assert.False(GateEPermit.TryAcquireFromFile(plan!, Now, out var repeated));
            Assert.Null(repeated);
            Assert.False(W1PreflightPlan.TryConsume(plan, inert, Now));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("report.txt")]
    [InlineData(@"artifacts\report.txt")]
    [InlineData(@"D:report.txt")]
    [InlineData(@"\artifacts\report.txt")]
    public void ReportDestination_RejectsPathsDependentOnCurrentDirectory(string? path)
    {
        var plan = CreatePlan() with { NewReportDestination = path };
        Assert.False(plan.InputsValid(Now));
        var error = Assert.Throws<InvalidOperationException>(() => plan.SerializeCanonical());
        Assert.Contains("fully qualified", error.Message);
    }

    [Fact]
    public void SerializeCanonical_MatchesLiteralFifteenFieldBytes_WithoutHashing()
    {
        // Frozen independent literal: LE int32 string lengths, strict UTF-8 (no BOM),
        // LE int64 Unix seconds, one-byte booleans and LE int32 limits.
        // The path is synthetic serialization input only; it is never opened.
        byte[] expected = Convert.FromHexString(
            "0D0000003139322E3136382E312E313030" +
            "10000000434E3D476174654554657374526F6F74" +
            "0A00000030314132423343344435" +
            "5005E56A00000000" + "A807E56A00000000" +
            "01" + "0F000000" + "00" + "00" + "01" + "01" +
            "00100000" + "20000000" + "00100000" +
            "2A000000443A5C50726F6A656374735C57312D476174652D425C6172746966616374735C7265706F72742E747874");
        var plan = CreatePlan();
        byte[] first = plan.SerializeCanonical();
        Assert.Equal(expected, first);
        first[0] ^= 0xFF;
        Assert.Equal(expected, plan.SerializeCanonical());
        Assert.Equal(expected, (plan with { NewReportDestination = @"D:\Projects\W1-Gate-B\artifacts\report.txt\" }).SerializeCanonical());
    }
}
