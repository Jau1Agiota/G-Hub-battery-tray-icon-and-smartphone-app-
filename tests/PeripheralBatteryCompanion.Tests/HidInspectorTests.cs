using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using PeripheralBatteryCompanion.G305Inspector;
using Xunit;

namespace PeripheralBatteryCompanion.Tests;

public sealed class HidInspectorTests
{
    private const string HidGuid = "{4d1e55b2-f16f-11cf-88cb-001111000030}";
    private static string PathFor(string hardware = "vid_046d&pid_c53f&mi_02&col01", string instance = "synthetic-instance") =>
        $@"\\?\hid#{hardware}#{instance}#{HidGuid}";
    private static HidCapabilities Caps(ushort usage = 1) => new(0xFF00, usage, 20, 21, 0, 3,
        new(1, 2, 3), new(4, 5, 6), new(0, 0, 0));

    [Theory]
    [InlineData("vid_046d&pid_c53f&mi_02&col01", ReceiverCollection.Col01)]
    [InlineData("VID_046D&PID_C53F&MI_02&COL02", ReceiverCollection.Col02)]
    [InlineData("col03&mi_02&pid_c53f&vid_046d", ReceiverCollection.Col03)]
    public void FilterAcceptsOnlyExactCandidateCollections(string hardware, ReceiverCollection expected)
    {
        Assert.True(ReceiverFilter.TryMatch(PathFor(hardware), out var found));
        Assert.Equal(expected, found);
    }

    [Theory]
    [InlineData("vid_046d&pid_c547&mi_02&col01")]
    [InlineData("vid_1234&pid_c53f&mi_02&col01")]
    [InlineData("vid_046d&pid_c53f&mi_01&col01")]
    [InlineData("vid_046d&pid_c53f&mi_02&col04")]
    [InlineData("vid_046d&pid_c53f&mi_02&col010")]
    [InlineData("vid_046d0&pid_c53f&mi_02&col01")]
    [InlineData("vid_046d&pid_c53f0&mi_02&col01")]
    [InlineData("vid_046d&pid_c53f&mi_020&col01")]
    [InlineData("vid_046d&pid_c53f&col01")]
    [InlineData("vid_046d&vid_1234&pid_c53f&mi_02&col01")]
    [InlineData("vid_046d&pid_c53f&mi_02&col01&col02")]
    public void FilterRejectsOtherDevicesInterfacesAndSubstringLookalikes(string hardware) =>
        Assert.False(ReceiverFilter.TryMatch(PathFor(hardware), out _));

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("not-a-device")]
    [InlineData(@"\\?\usb#vid_046d&pid_c53f&mi_02&col01#synthetic#{4d1e55b2-f16f-11cf-88cb-001111000030}")]
    [InlineData(@"\\?\hid#vid_046d&pid_c53f&mi_02&col01")]
    [InlineData(@"\\?\hid#vid_046d&pid_c53f&mi_02&col01##invalid-guid")]
    public void FilterRejectsMissingOrMalformedInterfacePaths(string? path) =>
        Assert.False(ReceiverFilter.TryMatch(path, out _));

    [Fact]
    public void InspectorOpensOnlyMatchingPathsAndDeduplicatesWithoutLeakingIdentifiers()
    {
        var target = PathFor(instance: "SYNTHETIC-PRIVATE-INSTANCE");
        var backend = new FakeBackend([PathFor("vid_046d&pid_c547&mi_02&col01"), target, target.ToUpperInvariant()]);
        var report = new CapabilityInspector(backend).Inspect();
        Assert.Equal([target], backend.Opened);
        Assert.Single(report.Collections);
        Assert.Equal(0, report.ExitCode);
        var summary = ReportFormatter.Format(report);
        Assert.DoesNotContain("SYNTHETIC-PRIVATE-INSTANCE", summary);
        Assert.DoesNotContain(target, summary);
        Assert.Contains("[instance omitted]", summary);
        Assert.Contains("COL02: not observed", summary);
    }

    [Fact]
    public void FormattingRetainsActualNumericCapabilitiesIncludingZeroLengthReports()
    {
        var report = new InspectionReport([new(ReceiverCollection.Col01, Caps(), null)], []);
        var summary = ReportFormatter.Format(report);
        Assert.Contains("Usage page: 0xFF00; Usage ID: 0x0001", summary);
        Assert.Contains("input=20, output=21, feature=0", summary);
        Assert.Contains("Input:   1 / 2 / 3", summary);
        Assert.Contains("Output:  4 / 5 / 6", summary);
        Assert.Contains("Feature: 0 / 0 / 0", summary);
        Assert.Contains("Link collection nodes: 3", summary);
        Assert.Contains("No battery protocol, battery level, connectivity", summary);
        Assert.DoesNotContain("battery: 45", summary, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(ReceiverCollection.Col01, 1)]
    [InlineData(ReceiverCollection.Col02, 2)]
    [InlineData(ReceiverCollection.Col03, 4)]
    public void ExpectedUsagesMatchSuppliedResearch(ReceiverCollection collection, ushort usage)
    {
        Assert.Equal(usage, ReceiverFilter.ExpectedUsage(collection));
        var summary = ReportFormatter.Format(new([new(collection, Caps(usage), null)], []));
        Assert.DoesNotContain("differs from supplied expectation", summary);
    }

    [Fact]
    public void UnexpectedUsageIsReportedRatherThanReplacedOrInterpretedAsBattery()
    {
        var summary = ReportFormatter.Format(new([new(ReceiverCollection.Col01, Caps(7) with { UsagePage = 0x0001 }, null)], []));
        Assert.Contains("Usage page: 0x0001; Usage ID: 0x0007", summary);
        Assert.Contains("differs from supplied expectation FF00:0001", summary);
    }

    [Fact]
    public void ACollectionErrorDoesNotPreventOtherCollectionsBeingInspected()
    {
        var backend = new FakeBackend([PathFor(), PathFor("vid_046d&pid_c53f&mi_02&col02")]);
        backend.Read = path => path.Contains("col01") ? new(null, new(InspectionStep.OpenInterface, 5)) : new(Caps(2), null);
        var report = new CapabilityInspector(backend).Inspect();
        Assert.Equal(2, backend.Opened.Count);
        Assert.Equal(1, report.ExitCode);
        Assert.NotNull(report.Collections[1].Capabilities);
        var summary = ReportFormatter.Format(report);
        Assert.Contains("OpenInterface; Win32=5", summary);
        Assert.Contains("no fallback attempted", summary);
    }

    [Fact]
    public void EnumerationErrorsRemainVisibleAlongsidePartialResults()
    {
        var backend = new FakeBackend([PathFor()]) { EnumerationFailures = [new(InspectionStep.InterfaceDetail, 13)] };
        var report = new CapabilityInspector(backend).Inspect();
        Assert.Single(report.Collections);
        Assert.Equal(1, report.ExitCode);
        Assert.Contains("InterfaceDetail; Win32=13", ReportFormatter.Format(report));
    }

    [Fact]
    public void Win32ExceptionMessagesAreNeverCopiedIntoReports()
    {
        var backend = new FakeBackend([PathFor()]) { Read = _ => throw new Win32Exception(5, "SYNTHETIC-SECRET-PATH") };
        var report = new CapabilityInspector(backend).Inspect();
        Assert.Equal(1, report.ExitCode);
        Assert.DoesNotContain("SYNTHETIC-SECRET-PATH", ReportFormatter.Format(report));
        backend.EnumerationError = new Win32Exception(5, "SYNTHETIC-SECRET-PATH");
        report = new CapabilityInspector(backend).Inspect();
        Assert.Empty(report.Collections);
        Assert.Contains("EnumerateInterfaces; Win32=5", ReportFormatter.Format(report));
        Assert.DoesNotContain("SYNTHETIC-SECRET-PATH", ReportFormatter.Format(report));
    }

    [Fact]
    public void HidStatusIsAnNtStatusNotAWin32SuccessCode()
    {
        var status = unchecked((int)0xC0110001);
        var report = new InspectionReport([new(ReceiverCollection.Col01, null, new(InspectionStep.GetCapabilities, HidStatus: status))], []);
        Assert.Equal(0x00110000, NativeMethods.HidpStatusSuccess);
        Assert.Equal(1, report.ExitCode);
        Assert.Contains("HID status=0xC0110001", ReportFormatter.Format(report));
    }

    [Fact]
    public void MissingResultsAndNoMatchingDevicesAreDistinct()
    {
        var backend = new FakeBackend([]);
        Assert.Equal(3, new CapabilityInspector(backend).Inspect().ExitCode);
        backend = new FakeBackend([PathFor()]) { Read = _ => new(null, null) };
        Assert.Equal(1, new CapabilityInspector(backend).Inspect().ExitCode);
        Assert.Contains("UnexpectedResult", ReportFormatter.Format(new CapabilityInspector(backend).Inspect()));
        Assert.Equal(2, new InspectionReport([], [new(InspectionStep.UnsupportedPlatform)]).ExitCode);
    }

    [Fact]
    public void MultipleReceiversAreNotSilentlyMergedByCollectionNumber()
    {
        var backend = new FakeBackend([PathFor(instance: "synthetic-a"), PathFor(instance: "synthetic-b")]);
        var report = new CapabilityInspector(backend).Inspect();
        Assert.Equal(2, report.Collections.Count);
        var text = ReportFormatter.Format(report);
        Assert.Contains("COL01 interface 1", text);
        Assert.Contains("COL01 interface 2", text);
        Assert.DoesNotContain("synthetic-a", text);
        Assert.DoesNotContain("synthetic-b", text);
    }

    [Fact]
    public void InteropLayoutsAndImportsAreLimitedToMetadataOperations()
    {
        Assert.Equal(64, Marshal.SizeOf<NativeMethods.HidpCaps>());
        Assert.Equal(44, Marshal.OffsetOf<NativeMethods.HidpCaps>(nameof(NativeMethods.HidpCaps.NumberLinkCollectionNodes)).ToInt32());
        Assert.Equal(IntPtr.Size == 8 ? 32 : 28, Marshal.SizeOf<NativeMethods.DeviceInterfaceData>());
        Assert.Equal(IntPtr.Size == 8 ? 8 : 6, NativeMethods.DetailHeaderSize);
        Assert.Equal(4, NativeMethods.DetailPathOffset);
        var allowed = new[] { "HidD_GetHidGuid", "HidD_GetPreparsedData", "HidD_FreePreparsedData", "HidP_GetCaps",
            "SetupDiGetClassDevsW", "SetupDiEnumDeviceInterfaces", "SetupDiGetDeviceInterfaceDetailW", "SetupDiDestroyDeviceInfoList", "CreateFileW" };
        var imported = typeof(NativeMethods).GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Select(method => method.GetCustomAttribute<DllImportAttribute>()).OfType<DllImportAttribute>()
            .Select(attribute => attribute.EntryPoint).Order().ToArray();
        Assert.Equal(allowed.Order().ToArray(), imported);
    }

    private sealed class FakeBackend(IReadOnlyList<string> paths) : IHidMetadataBackend
    {
        public List<string> Opened { get; } = [];
        public IReadOnlyList<InspectionFailure> EnumerationFailures { get; init; } = [];
        public Win32Exception? EnumerationError { get; set; }
        public Func<string, CapabilityRead> Read { get; set; } = _ => new(Caps(), null);
        public InterfaceEnumeration EnumerateInterfaces() => EnumerationError is not null
            ? throw EnumerationError : new(paths, EnumerationFailures);
        public CapabilityRead ReadCapabilities(string interfacePath) { Opened.Add(interfacePath); return Read(interfacePath); }
    }
}
