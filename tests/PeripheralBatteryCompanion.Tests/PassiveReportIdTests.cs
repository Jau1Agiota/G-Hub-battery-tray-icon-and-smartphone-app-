using System.Reflection;
using System.Runtime.InteropServices;
using PeripheralBatteryCompanion.G305Inspector;
using Xunit;

namespace PeripheralBatteryCompanion.Tests;

public sealed class PassiveReportIdTests
{
    [Fact]
    public void ExtractsDistinctSortedIdsWithoutTreatingZeroAsMissing()
    {
        var result = PassiveCapabilityReader.Extract(4, n => (NativeMethods.HidpStatusSuccess, n, [0x11, 0, 0x10, 0x11]));
        Assert.Equal(ReportIdResult.Available, result.Result);
        Assert.Equal(new byte[] { 0, 0x10, 0x11 }, result.Ids);
        Assert.Equal(4, result.Returned);
    }

    [Theory]
    [InlineData(0, ReportIdResult.NotEstablished)]
    [InlineData(257, ReportIdResult.LimitExceeded)]
    [InlineData(65535, ReportIdResult.LimitExceeded)]
    public void EmptyAndExcessiveCountsDoNotCallNativeApi(ushort count, ReportIdResult expected)
    {
        var result = PassiveCapabilityReader.Extract(count, _ => throw new InvalidOperationException("must not call"));
        Assert.Equal(expected, result.Result);
        Assert.Empty(result.Ids);
    }

    [Fact]
    public void MaximumPermittedCountIsBoundedTo256DistinctIds()
    {
        var result = PassiveCapabilityReader.Extract(256, n => (NativeMethods.HidpStatusSuccess, n, Enumerable.Range(0, 256).Select(i => (byte)i).ToArray()));
        Assert.Equal(256, result.Ids.Count);
        Assert.Equal(ReportIdResult.Available, result.Result);
    }

    [Theory]
    [InlineData(0, 0)] [InlineData(1, 1)] [InlineData(3, 2)] [InlineData(2, 1)]
    public void InconsistentReturnedCountsFailClosed(ushort returned, int bytes)
    {
        var result = PassiveCapabilityReader.Extract(2, _ => (NativeMethods.HidpStatusSuccess, returned, new byte[bytes]));
        Assert.Equal(ReportIdResult.Malformed, result.Result);
        Assert.Empty(result.Ids);
    }

    [Fact]
    public void FailedApiNeverUsesPartiallyWrittenArray()
    {
        var result = PassiveCapabilityReader.Extract(2, _ => (unchecked((int)0xC0110004), 4, new byte[] { 0x10, 0x11 }));
        Assert.Equal(ReportIdResult.NativeFailure, result.Result);
        Assert.Empty(result.Ids);
        Assert.Equal(unchecked((int)0xC0110004), result.HidStatus);
        Assert.Equal(1, Report(result).ExitCode);
        Assert.Contains("0xC0110004", ReportFormatter.Format(Report(result)));
    }

    [Fact]
    public void LayoutsMatchWindowsSdkOpaqueCapabilityArrays()
    {
        Assert.Equal(72, Marshal.SizeOf<PassiveMetadataNative.ButtonCaps>());
        Assert.Equal(72, Marshal.SizeOf<PassiveMetadataNative.ValueCaps>());
        Assert.Equal(2, Marshal.OffsetOf<PassiveMetadataNative.ButtonCaps>("ReportId").ToInt32());
        Assert.Equal(2, Marshal.OffsetOf<PassiveMetadataNative.ValueCaps>("ReportId").ToInt32());
        Assert.Equal(IntPtr.Size == 8 ? 32 : 28, Marshal.SizeOf<PassiveMetadataNative.DeviceInfo>());
        Assert.Equal(20, Marshal.OffsetOf<PassiveMetadataNative.DeviceInfo>("DevInst").ToInt32());
    }

    [Theory]
    [InlineData(@"USB\VID_046D&PID_C53F&MI_02\private", true)]
    [InlineData(@"usb\mi_02&pid_c53f&vid_046d\private", true)]
    [InlineData(@"USB\VID_046D&PID_C547&MI_02\private", false)]
    [InlineData(@"USB\VID_046D&PID_C53F&MI_020\private", false)]
    [InlineData(@"HID\VID_046D&PID_C53F&MI_02\private", false)]
    [InlineData(@"USB\VID_046D&PID_C53F&MI_02&COL01\private", false)]
    [InlineData(@"USB\VID_046D&PID_C53F&MI_02\", false)]
    public void ParentMatcherRequiresExactUsbInterface(string id, bool match) =>
        Assert.Equal(match, PassiveParentGrouping.IsTargetUsbInterface(id));

    [Fact]
    public void FormatSeparatesDirectionsAndWarnsOfUnknownsAndMultipleParents()
    {
        var ids = PassiveCapabilityReader.Extract(1, n => (NativeMethods.HidpStatusSuccess, n, new byte[] { 0x10 }));
        var report = Report(ids);
        report = report with { Collections = [report.Collections[0], report.Collections[0] with { ParentGroup = 2 }] };
        var text = ReportFormatter.Format(report);
        Assert.Contains("P1 (invocation only)", text);
        Assert.Contains("P2 (invocation only)", text);
        Assert.Contains("Input buttons: 0x10", text);
        Assert.Contains("Output values: not established", text);
        Assert.Contains("Multiple receiver parents: inconclusive", text);
        Assert.Contains("remain UNKNOWN", text);
        Assert.Contains("ID 0 may mean no ID", text);
        Assert.DoesNotContain("PRIVATE", text);
    }

    [Fact]
    public void EnumerationGroupingIsCopiedAsAnonymousNumbersWithoutOpeningWrongHardware()
    {
        var backend = new FakeBackend();
        var report = new CapabilityInspector(backend).Inspect();
        Assert.Single(report.Collections);
        Assert.Equal(7, report.Collections[0].ParentGroup);
        Assert.Single(backend.Opened);
        Assert.DoesNotContain("PRIVATE", ReportFormatter.Format(report));
    }

    [Fact]
    public void NewNativeImportsAreOnlyCapabilityAndPnpMetadata()
    {
        var actual = typeof(PassiveMetadataNative).GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Select(m => m.GetCustomAttribute<DllImportAttribute>()).OfType<DllImportAttribute>().Select(a => a.EntryPoint).Order().ToArray();
        Assert.Equal(new[] { "CM_Get_Device_IDW", "CM_Get_Parent", "HidP_GetButtonCaps", "HidP_GetValueCaps", "SetupDiGetDeviceInterfaceDetailW" }.Order(), actual);
        Assert.DoesNotContain(typeof(PassiveMetadataNative).Assembly.GetReferencedAssemblies(), a => a.Name!.Contains("HyperX"));
    }

    private static InspectionReport Report(CapabilityReportIds ids)
    {
        var empty = new CapabilityReportIds(0, 0, [], ReportIdResult.NotEstablished);
        var caps = new HidCapabilities(0xFF00, 1, 7, 7, 0, 1, new(1, 0, 1), new(0, 0, 0), new(0, 0, 0), new(new(ids, empty), new(empty, empty)));
        return new([new(ReceiverCollection.Col01, caps, null, 1)], []);
    }

    private sealed class FakeBackend : IHidMetadataBackend
    {
        private const string Target = @"\\?\hid#vid_046d&pid_c53f&mi_02&col01#PRIVATE#{4d1e55b2-f16f-11cf-88cb-001111000030}";
        public List<string> Opened { get; } = [];
        public InterfaceEnumeration EnumerateInterfaces() => new([Target, Target.Replace("c53f", "c547"), Target.Replace("col01", "col04")], [], new Dictionary<string, int> { [Target] = 7 });
        public CapabilityRead ReadCapabilities(string path) { Opened.Add(path); return new(Report(new(0, 0, [], ReportIdResult.NotEstablished)).Collections[0].Capabilities, null); }
    }
}
