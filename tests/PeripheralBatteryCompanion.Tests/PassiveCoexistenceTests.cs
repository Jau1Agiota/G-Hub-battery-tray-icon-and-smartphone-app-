using PeripheralBatteryCompanion.G305Inspector;
using PeripheralBatteryCompanion.HyperXBatteryPoc;
using Xunit;

namespace PeripheralBatteryCompanion.Tests;

public sealed class PassiveCoexistenceTests
{
    private const string Target = @"\\?\hid#vid_03f0&pid_05b7&mi_03&col01#private-instance#{4d1e55b2-f16f-11cf-88cb-001111000030}";
    private static readonly HidCapabilities Caps = new(0xFF13, 1, 62, 62, 0, 0, new(0, 0, 0), new(0, 0, 0), new(0, 0, 0));
    private sealed class Metadata : IHidMetadataBackend
    {
        public InterfaceEnumeration Enumeration = new([Target], []);
        public CapabilityRead Read = new(Caps, null);
        public List<string> Inspected = [];
        public InterfaceEnumeration EnumerateInterfaces() => Enumeration;
        public CapabilityRead ReadCapabilities(string path) { Inspected.Add(path); return Read; }
    }
    private sealed class Probe : IPassiveSharedHandleProbe
    {
        public int? Error;
        public List<string> Opened = [];
        public int? OpenAndClose(string path) { Opened.Add(path); return Error; }
    }

    [Fact]
    public void ExplicitModeAndActiveConfirmationCannotBeCombined()
    {
        Assert.Equal(PocMode.PassiveCoexistence, PocPlan.Parse(["--passive-coexistence"]));
        Assert.Equal(PocMode.Invalid, PocPlan.Parse(["--passive-coexistence", "--read-battery"]));
        Assert.Equal(PocMode.Invalid, PocPlan.Parse(["--passive-coexistence", "--confirm-ngenuity-closed"]));
        Assert.Equal(PocMode.Invalid, PocPlan.Parse(["--passive-coexistence", "--preview-request"]));
        Assert.Equal(PocMode.Invalid, PocPlan.Parse(["--read-battery"]));
        Assert.Equal(PocMode.ReadBattery, PocPlan.Parse(["--read-battery", "--confirm-ngenuity-closed"]));
    }

    [Fact]
    public void SelectsOnlyExactCollectionAndReportsSharedOpenWithoutExposingInstance()
    {
        var metadata = new Metadata { Enumeration = new([Target.Replace("col01", "col02"), Target.Replace("mi_03", "mi_02"), Target, Target.ToUpperInvariant()], []) };
        var probe = new Probe();
        var result = new PassiveCoexistence(metadata, probe).Inspect();
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("SharedOpenSucceeded", result.Status);
        Assert.Equal(new[] { Target }, metadata.Inspected);
        Assert.Equal(new[] { Target }, probe.Opened);
        var text = PassiveCoexistence.Format(result);
        Assert.DoesNotContain("private-instance", text);
        Assert.Contains("0x80000000", text); Assert.Contains("0x00000003", text);
        Assert.Contains("no ReadFile", text);
        Assert.Equal(0x80000000u, PassiveCoexistence.DesiredAccess);
        Assert.Equal(3u, PassiveCoexistence.ShareMode);
        Assert.Equal(0x40000000u, PassiveCoexistence.OpenFlags);
        // The injectable probe cannot submit/read reports or create an active session.
        Assert.Single(typeof(IPassiveSharedHandleProbe).GetMethods());
    }

    [Theory]
    [InlineData(5)] [InlineData(32)]
    public void AccessOrSharingDenialPreservesErrorAndDoesNotRetry(int error)
    {
        var probe = new Probe { Error = error };
        var result = new PassiveCoexistence(new Metadata(), probe).Inspect();
        Assert.Equal(1, result.ExitCode); Assert.True(result.SharedOpenAttempted);
        Assert.Equal(error, result.OpenError); Assert.Single(probe.Opened);
        Assert.Contains($"Win32 open error={error}", PassiveCoexistence.Format(result));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void MissingOrAmbiguousTargetDoesNotOpenAnything(bool ambiguous)
    {
        var metadata = new Metadata { Enumeration = new(ambiguous ? [Target, Target.Replace("private-instance", "other")] : [], []) };
        var probe = new Probe();
        var result = new PassiveCoexistence(metadata, probe).Inspect();
        Assert.Equal(ambiguous ? 5 : 3, result.ExitCode);
        Assert.Empty(metadata.Inspected); Assert.Empty(probe.Opened);
    }

    [Fact]
    public void IncompleteEnumerationDoesNotSelectEvenAnObservedTarget()
    {
        var metadata = new Metadata { Enumeration = new([Target], [new(InspectionStep.EnumerateInterfaces, 5)]) };
        var probe = new Probe();
        var result = new PassiveCoexistence(metadata, probe).Inspect();
        Assert.Equal("EnumerationFailed", result.Status); Assert.Equal(5, result.Failure!.Win32Error);
        Assert.Empty(metadata.Inspected); Assert.Empty(probe.Opened);
    }

    [Fact]
    public void MetadataErrorRetainsHidStatusAndSkipsSharedOpen()
    {
        var metadata = new Metadata { Read = new(null, new(InspectionStep.GetCapabilities, HidStatus: -1)) };
        var probe = new Probe();
        var result = new PassiveCoexistence(metadata, probe).Inspect();
        Assert.Equal("MetadataFailed", result.Status); Assert.Equal(-1, result.Failure!.HidStatus);
        Assert.Empty(probe.Opened);
    }

    [Fact]
    public void UnexpectedCapabilitiesAreReportedButDoNotOpenReadHandle()
    {
        var metadata = new Metadata { Read = new(Caps with { UsagePage = 0xC }, null) };
        var probe = new Probe();
        var result = new PassiveCoexistence(metadata, probe).Inspect();
        Assert.Equal("CapabilitiesMismatch", result.Status); Assert.False(result.SharedOpenAttempted);
        Assert.Contains("0x000C", PassiveCoexistence.Format(result)); Assert.Empty(probe.Opened);
    }
}
