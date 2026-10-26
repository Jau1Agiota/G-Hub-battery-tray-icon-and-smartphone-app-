using System.ComponentModel;
using PeripheralBatteryCompanion.G305Inspector;
using Xunit;

namespace PeripheralBatteryCompanion.Tests;

public sealed class HyperXInspectorTests
{
    private static readonly HidInspectionTarget HyperX = HidInspectionTarget.HyperX;
    private const string HidGuid = "{4d1e55b2-f16f-11cf-88cb-001111000030}";
    private static string PathFor(string hardware = "vid_03f0&pid_05b7&mi_03&col01", string instance = "synthetic-instance") =>
        $@"\\?\hid#{hardware}#{instance}#{HidGuid}";
    private static HidCapabilities Caps(ReceiverCollection collection) => collection switch
    {
        ReceiverCollection.Col01 => new(0xFF13, 1, 65, 65, 32, 3, new(1, 2, 3), new(4, 5, 6), new(7, 8, 9)),
        ReceiverCollection.Col02 => new(0x000C, 1, 3, 0, 0, 1, new(2, 0, 2), new(0, 0, 0), new(0, 0, 0)),
        ReceiverCollection.Col03 => new(0x000B, 5, 8, 0, 0, 1, new(1, 1, 2), new(0, 0, 0), new(0, 0, 0)),
        _ => throw new ArgumentOutOfRangeException(nameof(collection))
    };

    [Theory]
    [InlineData("vid_03f0&pid_05b7&mi_03&col01", ReceiverCollection.Col01)]
    [InlineData("VID_03F0&PID_05B7&MI_03&COL02", ReceiverCollection.Col02)]
    [InlineData("col03&mi_03&pid_05b7&vid_03f0", ReceiverCollection.Col03)]
    [InlineData("vid_03f0&pid_05b7&rev_4109&mi_03&col01", ReceiverCollection.Col01)]
    [InlineData("vid_03f0&pid_05b7&rev_9999&mi_03&col01", ReceiverCollection.Col01)]
    public void ExactHyperXHardwareAndCollectionsMatchWithOrWithoutRevision(string hardware, ReceiverCollection expected)
    {
        Assert.True(ReceiverFilter.TryMatch(PathFor(hardware), HyperX, out var actual));
        Assert.Equal(expected, actual);
        Assert.False(ReceiverFilter.TryMatch(PathFor(hardware), out _)); // Default stays G305.
    }

    [Theory]
    [InlineData("vid_046d&pid_c53f&mi_02&col01")]
    [InlineData("vid_046d&pid_c547&mi_03&col01")]
    [InlineData("vid_03f0&pid_05b8&mi_03&col01")]
    [InlineData("vid_03f1&pid_05b7&mi_03&col01")]
    [InlineData("vid_03f0&pid_05b7&mi_02&col01")]
    [InlineData("vid_03f0&pid_05b7&mi_030&col01")]
    [InlineData("vid_03f00&pid_05b7&mi_03&col01")]
    [InlineData("vid_03f0&pid_05b70&mi_03&col01")]
    [InlineData("vid_03f0&pid_05b7&mi_03&col010")]
    [InlineData("vid_03f0&pid_05b7&mi_03&col04")]
    [InlineData("vid_03f0&pid_05b7&col01")]
    [InlineData("vid_03f0&pid_05b7&mi_03&col01&col02")]
    [InlineData("vid_03f0&vid_046d&pid_05b7&mi_03&col01")]
    public void OtherDevicesInterfacesAndLookalikesAreExcluded(string hardware) =>
        Assert.False(ReceiverFilter.TryMatch(PathFor(hardware), HyperX, out _));

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("not-a-device")]
    [InlineData(@"\\?\hid#vid_03f0&pid_05b7&mi_03&col01")]
    [InlineData(@"\\?\hid#vid_03f0&pid_05b7&mi_03&col01##invalid")]
    public void MalformedPathsAreRejected(string? path) => Assert.False(ReceiverFilter.TryMatch(path, HyperX, out _));

    [Fact]
    public void VendorCollectionIsInspectedFirstAndAllThreeAreReported()
    {
        var paths = new[] { PathFor("vid_03f0&pid_05b7&mi_03&col03"), PathFor("vid_03f0&pid_05b7&mi_03&col02"), PathFor() };
        var backend = new FakeBackend(paths);
        var report = new CapabilityInspector(backend, HyperX).Inspect();
        Assert.Equal(new[] { paths[2], paths[1], paths[0] }, backend.Opened);
        Assert.Equal(3, report.Collections.Count);
        Assert.Equal(0, report.ExitCode);
        var text = ReportFormatter.Format(report);
        Assert.Contains("HyperX Cloud III Wireless", text);
        Assert.Contains("VID 03F0 / PID 05B7 / MI_03", text);
        Assert.Contains("Reference USB hardware-ID revision: 4109 (supplied, not queried)", text);
        Assert.Contains("0xFF13; Usage ID: 0x0001", text);
        Assert.Contains("0x000C; Usage ID: 0x0001", text);
        Assert.Contains("0x000B; Usage ID: 0x0005", text);
        Assert.DoesNotContain("differs from supplied expectation", text);
        Assert.True(text.IndexOf("\nCOL01", StringComparison.Ordinal) < text.IndexOf("\nCOL02", StringComparison.Ordinal));
        Assert.True(text.IndexOf("\nCOL02", StringComparison.Ordinal) < text.IndexOf("\nCOL03", StringComparison.Ordinal));
    }

    [Fact]
    public void NumericCapabilitiesAreActualMetadataNotBatteryTelemetry()
    {
        var backend = new FakeBackend([PathFor()]);
        var text = ReportFormatter.Format(new CapabilityInspector(backend, HyperX).Inspect());
        Assert.Contains("input=65, output=65, feature=32", text); // Synthetic counts, not hardware findings.
        Assert.Contains("Input:   1 / 2 / 3", text);
        Assert.Contains("Output:  4 / 5 / 6", text);
        Assert.Contains("Feature: 7 / 8 / 9", text);
        Assert.Contains("Link collection nodes: 3", text);
        Assert.Contains("No battery protocol, battery level, connectivity", text);
    }

    [Fact]
    public void MissingCollectionsDifferFromAccessDeniedCollections()
    {
        var backend = new FakeBackend([PathFor()]);
        var available = new CapabilityInspector(backend, HyperX).Inspect();
        Assert.Equal(0, available.ExitCode); // One successful collection is not proof of three present.
        var text = ReportFormatter.Format(available);
        Assert.Contains("COL02: not observed", text);
        Assert.Contains("COL03: not observed", text);
        Assert.DoesNotContain("ERROR:", text);
        backend.Read = _ => new(null, new(InspectionStep.OpenInterface, 5));
        var denied = new CapabilityInspector(backend, HyperX).Inspect();
        Assert.Equal(1, denied.ExitCode);
        text = ReportFormatter.Format(denied);
        Assert.Contains("ERROR: OpenInterface; Win32=5", text);
        Assert.Contains("no fallback attempted", text);
        Assert.DoesNotContain("COL01: not observed", text);
    }

    [Fact]
    public void FailedPriorityCollectionDoesNotPreventComparingOtherCollections()
    {
        var backend = new FakeBackend([PathFor("vid_03f0&pid_05b7&mi_03&col03"), PathFor()]);
        backend.Read = path => path.Contains("col01", StringComparison.OrdinalIgnoreCase)
            ? new(null, new(InspectionStep.GetCapabilities, HidStatus: unchecked((int)0xC0110001)))
            : new(Caps(ReceiverCollection.Col03), null);
        var report = new CapabilityInspector(backend, HyperX).Inspect();
        Assert.Equal(2, backend.Opened.Count);
        Assert.Equal(1, report.ExitCode);
        Assert.Contains("col01", backend.Opened[0]);
        var text = ReportFormatter.Format(report);
        Assert.Contains("HID status=0xC0110001", text);
        Assert.Contains("0x000B; Usage ID: 0x0005", text);
    }

    [Fact]
    public void PartialEnumerationAndExceptionErrorsKeepHyperXScopeAndSanitizeIdentifiers()
    {
        const string privateInstance = "SYNTHETIC-PRIVATE-HYPERX-INSTANCE";
        var path = PathFor(instance: privateInstance);
        var backend = new FakeBackend([path, path.ToUpperInvariant()])
        { EnumerationFailures = [new(InspectionStep.InterfaceDetail, 13)] };
        backend.Read = _ => throw new Win32Exception(5, privateInstance);
        var report = new CapabilityInspector(backend, HyperX).Inspect();
        Assert.Single(backend.Opened);
        Assert.Equal(1, report.ExitCode);
        var text = ReportFormatter.Format(report);
        Assert.Contains("HyperX Cloud III Wireless", text);
        Assert.Contains("[instance omitted]", text);
        Assert.DoesNotContain(privateInstance, text);
        Assert.DoesNotContain(path, text);
        Assert.Contains("ReadCollection; Win32=5", text);
        Assert.Contains("InterfaceDetail; Win32=13", text);
        backend.EnumerationError = new Win32Exception(5, privateInstance);
        text = ReportFormatter.Format(new CapabilityInspector(backend, HyperX).Inspect());
        Assert.Contains("HyperX Cloud III Wireless", text);
        Assert.Contains("EnumerateInterfaces; Win32=5", text);
        Assert.DoesNotContain(privateInstance, text);
    }

    [Fact]
    public void UnexpectedUsageIsNotSubstitutedByTheProfile()
    {
        var backend = new FakeBackend([PathFor()])
        { Read = _ => new(Caps(ReceiverCollection.Col01) with { UsagePage = 0xFF00, Usage = 7 }, null) };
        var text = ReportFormatter.Format(new CapabilityInspector(backend, HyperX).Inspect());
        Assert.Contains("Usage page: 0xFF00; Usage ID: 0x0007", text);
        Assert.Contains("expectation FF13:0001; actual metadata retained", text);
    }

    [Fact]
    public void MultipleReceiversRemainSeparateWithoutRawInstanceIdentifiers()
    {
        var backend = new FakeBackend([PathFor(instance: "synthetic-hyperx-a"), PathFor(instance: "synthetic-hyperx-b")]);
        var report = new CapabilityInspector(backend, HyperX).Inspect();
        Assert.Equal(2, report.Collections.Count);
        var text = ReportFormatter.Format(report);
        Assert.Contains("COL01 interface 1", text);
        Assert.Contains("COL01 interface 2", text);
        Assert.DoesNotContain("synthetic-hyperx-", text);
    }

    [Fact]
    public void MixedEnumerationIsIsolatedPerProfileAndG305DefaultsAreUnchanged()
    {
        var g305 = PathFor("vid_046d&pid_c53f&mi_02&col01");
        var g915 = PathFor("vid_046d&pid_c547&mi_02&col01");
        var paths = new[] { PathFor(), g305, g915 };
        var defaultBackend = new FakeBackend(paths);
        defaultBackend.Read = _ => new(Caps(ReceiverCollection.Col01) with { UsagePage = 0xFF00 }, null);
        var defaultReport = new CapabilityInspector(defaultBackend).Inspect();
        Assert.Equal(new[] { g305 }, defaultBackend.Opened);
        var explicitBackend = new FakeBackend(paths) { Read = defaultBackend.Read };
        Assert.Equal(ReportFormatter.Format(defaultReport),
            ReportFormatter.Format(new CapabilityInspector(explicitBackend, HidInspectionTarget.G305).Inspect()));
        var text = ReportFormatter.Format(defaultReport);
        Assert.StartsWith("Peripheral Battery Companion - Passive G305 HID Capability Inspector", text);
        Assert.Contains("VID 046D / PID C53F / MI_02", text);
        Assert.DoesNotContain("HyperX", text);
        Assert.DoesNotContain("Reference USB hardware-ID revision", text);
        var hyperxBackend = new FakeBackend(paths);
        Assert.Equal(0, new CapabilityInspector(hyperxBackend, HyperX).Inspect().ExitCode);
        Assert.Equal(new[] { paths[0] }, hyperxBackend.Opened);
        var noMatch = new CapabilityInspector(new FakeBackend([g305, g915]), HyperX).Inspect();
        Assert.Equal(3, noMatch.ExitCode);
        Assert.Empty(noMatch.Collections);
    }

    [Fact]
    public void MissingBackendResultIsStillAnInspectionFailure()
    {
        var backend = new FakeBackend([PathFor()]) { Read = _ => new(null, null) };
        var report = new CapabilityInspector(backend, HyperX).Inspect();
        Assert.Equal(1, report.ExitCode);
        Assert.Contains("UnexpectedResult", ReportFormatter.Format(report));
    }

    [Fact]
    public void CliDefaultsToG305AndOnlyAllowsTheClosedHyperXSelection()
    {
        Assert.True(Program.TrySelectTarget([], out var g305));
        Assert.Same(HidInspectionTarget.G305, g305);
        Assert.True(Program.TrySelectTarget(["--hyperx"], out var hyperx));
        Assert.Same(HyperX, hyperx);
        Assert.False(Program.TrySelectTarget(["--vid", "03F0"], out _));
        Assert.False(Program.TrySelectTarget(["--hyperx", "--read"], out _));
        Assert.False(Program.TrySelectTarget(["--write"], out _));
    }

    private sealed class FakeBackend(IReadOnlyList<string> paths) : IHidMetadataBackend
    {
        public List<string> Opened { get; } = [];
        public IReadOnlyList<InspectionFailure> EnumerationFailures { get; init; } = [];
        public Win32Exception? EnumerationError { get; set; }
        public Func<string, CapabilityRead> Read { get; set; } = path =>
            new(Caps(path.Contains("col03", StringComparison.OrdinalIgnoreCase) ? ReceiverCollection.Col03 :
                path.Contains("col02", StringComparison.OrdinalIgnoreCase) ? ReceiverCollection.Col02 : ReceiverCollection.Col01), null);
        public InterfaceEnumeration EnumerateInterfaces() => EnumerationError is not null
            ? throw EnumerationError : new(paths, EnumerationFailures);
        public CapabilityRead ReadCapabilities(string path) { Opened.Add(path); return Read(path); }
    }
}
