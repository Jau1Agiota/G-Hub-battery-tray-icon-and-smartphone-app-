using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using PeripheralBatteryCompanion.G305Inspector;
using PassiveNative = PeripheralBatteryCompanion.G305Inspector.NativeMethods;

namespace PeripheralBatteryCompanion.HyperXBatteryPoc;

// This boundary deliberately exposes no report-read or report-submission operation.
public interface IPassiveSharedHandleProbe
{
    int? OpenAndClose(string path);
}

public sealed record PassiveCoexistenceResult(string Status, int ExitCode, HidCapabilities? Capabilities = null,
    InspectionFailure? Failure = null, bool SharedOpenAttempted = false, int? OpenError = null);

public sealed class PassiveCoexistence(IHidMetadataBackend metadata, IPassiveSharedHandleProbe probe)
{
    public const uint DesiredAccess = 0x80000000; // GENERIC_READ: same minimum access as active input handle.
    public const uint ShareMode = 3; // FILE_SHARE_READ | FILE_SHARE_WRITE.
    public const uint OpenFlags = 0x40000000; // FILE_FLAG_OVERLAPPED; no I/O is submitted.

    public PassiveCoexistenceResult Inspect()
    {
        var enumeration = metadata.EnumerateInterfaces();
        if (enumeration.Failures.Count > 0) return new("EnumerationFailed", 1, Failure: enumeration.Failures[0]);
        string path;
        try { path = HyperXReceiverSelection.SelectSinglePath(enumeration.Paths); }
        catch (AttemptRefusedException error)
        {
            return new(error.Status.ToString(), error.Status == AttemptStatus.NoReceiver ? 3 : 5);
        }
        var read = metadata.ReadCapabilities(path); // Zero-access handle, closed by metadata backend.
        if (read.Failure is not null || read.Capabilities is null)
            return new("MetadataFailed", 1, read.Capabilities, read.Failure ?? new(InspectionStep.UnexpectedResult));
        if (!HyperXReceiverSelection.HasExpectedCapabilities(read.Capabilities))
            return new("CapabilitiesMismatch", 1, read.Capabilities);
        var errorCode = probe.OpenAndClose(path);
        return new(errorCode.HasValue ? "SharedOpenFailed" : "SharedOpenSucceeded", errorCode.HasValue ? 1 : 0,
            read.Capabilities, SharedOpenAttempted: true, OpenError: errorCode);
    }

    public static string Format(PassiveCoexistenceResult result)
    {
        var report = new InspectionReport(result.Capabilities is null ? [] :
            [new(ReceiverCollection.Col01, result.Capabilities, null)], [], HidInspectionTarget.HyperX);
        return "HyperX passive coexistence probe: VID_03F0 PID_05B7 MI_03 COL01\n" +
            "NGENUITY may remain open. No report reads, writes, feature requests or battery queries.\n" +
            "Metadata open: desired access=0x00000000; sharing=0x00000003 (READ|WRITE).\n" +
            $"Shared probe: desired access=0x{DesiredAccess:X8} (GENERIC_READ); sharing=0x{ShareMode:X8} (READ|WRITE); flags=0x{OpenFlags:X8} (OVERLAPPED); OPEN_EXISTING=3.\n" +
            "GENERIC_READ tests the active handle's access compatibility only; no ReadFile is called.\n" +
            $"Discovery/inspection result: {result.Status}; shared open attempted={result.SharedOpenAttempted}; " +
            $"Win32 open error={(result.OpenError.HasValue ? result.OpenError.Value.ToString() : "none")}.\n" +
            (result.Failure is null ? "" : $"Metadata/enumeration error: {result.Failure.Step}; Win32={result.Failure.Win32Error}; HID={result.Failure.HidStatus}.\n") +
            (result.Capabilities is null ? "" : "Metadata phase report (zero-access handle):\n" + ReportFormatter.Format(report) + "\n") +
            $"Result: {result.Status}; exit code={result.ExitCode}. All owned handles closed before return; no permissions fallback.\n" +
            "This tests discovery, metadata and shared handle access only; response delivery/correlation and active coexistence remain unverified.";
    }
}

[SupportedOSPlatform("windows")]
internal sealed class WindowsPassiveSharedHandleProbe : IPassiveSharedHandleProbe
{
    public int? OpenAndClose(string path)
    {
        using var handle = PassiveNative.CreateFile(path, PassiveCoexistence.DesiredAccess,
            PassiveCoexistence.ShareMode, IntPtr.Zero, 3, PassiveCoexistence.OpenFlags, IntPtr.Zero);
        return handle.IsInvalid ? Marshal.GetLastWin32Error() : null;
    }
}
