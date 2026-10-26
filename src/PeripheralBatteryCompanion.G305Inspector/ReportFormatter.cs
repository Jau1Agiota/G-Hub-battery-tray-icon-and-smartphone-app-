using System.Globalization;
using System.Text;

namespace PeripheralBatteryCompanion.G305Inspector;

public static class ReportFormatter
{
    public static string Format(InspectionReport report)
    {
        var text = new StringBuilder();
        text.AppendLine("Peripheral Battery Companion - Passive G305 HID Capability Inspector");
        text.AppendLine("Scope: VID 046D / PID C53F / MI_02 / COL01-COL03 (G915 C547 excluded)");
        text.AppendLine("Metadata only: no feature/input/output reports requested or transmitted.");
        text.AppendLine("Access: zero desired access, share read/write; no permission fallback.");
        text.AppendLine("Interface instance identifiers omitted from terminal and clipboard.");
        text.AppendLine($"Matching interfaces: {Number(report.Collections.Count)}");
        foreach (var collection in Enum.GetValues<ReceiverCollection>())
        {
            var matches = report.Collections.Where(item => item.Collection == collection).ToArray();
            if (matches.Length == 0) { text.AppendLine($"{Label(collection)}: not observed"); continue; }
            for (var index = 0; index < matches.Length; index++)
            {
                text.AppendLine();
                text.AppendLine($"{Label(collection)}{(matches.Length > 1 ? $" interface {Number(index + 1)}" : "")}");
                text.AppendLine($@"  Path: \\?\hid#vid_046d&pid_c53f&mi_02&{Label(collection).ToLowerInvariant()}#[instance omitted]#[HID class GUID]");
                var item = matches[index];
                if (item.Failure is not null) { text.AppendLine("  " + FormatFailure(item.Failure)); continue; }
                if (item.Capabilities is not HidCapabilities caps) { text.AppendLine("  ERROR: no capabilities returned"); continue; }
                text.AppendLine($"  Usage page: 0x{caps.UsagePage:X4}; Usage ID: 0x{caps.Usage:X4}");
                text.AppendLine($"  Report bytes: input={Number(caps.InputReportBytes)}, output={Number(caps.OutputReportBytes)}, feature={Number(caps.FeatureReportBytes)}");
                text.AppendLine("  Capability counts (buttons / values / data indices):");
                text.AppendLine($"    Input:   {Counts(caps.Input)}");
                text.AppendLine($"    Output:  {Counts(caps.Output)}");
                text.AppendLine($"    Feature: {Counts(caps.Feature)}");
                text.AppendLine($"  Link collection nodes: {Number(caps.LinkCollectionNodes)}");
                if (caps.UsagePage != 0xFF00 || caps.Usage != ReceiverFilter.ExpectedUsage(collection))
                    text.AppendLine($"  NOTE: differs from supplied expectation FF00:{ReceiverFilter.ExpectedUsage(collection):X4}; actual metadata retained.");
            }
        }
        foreach (var failure in report.EnumerationFailures) text.AppendLine(FormatFailure(failure));
        text.AppendLine();
        text.AppendLine(report.ExitCode switch
        {
            0 => "Result: metadata inspection completed for the matching interfaces above.",
            2 => "Result: NOT RUN - Windows is required.",
            3 => "Result: no matching receiver collection interfaces found.",
            _ => "Result: incomplete - one or more metadata operations failed."
        });
        text.AppendLine("No battery protocol, battery level, connectivity or physical-parent grouping is inferred.");
        text.AppendLine($"Exit code: {Number(report.ExitCode)}");
        return text.ToString().TrimEnd();
    }

    private static string Label(ReceiverCollection collection) => $"COL{(int)collection:00}";
    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Counts(ReportCapabilityCounts caps) => $"{Number(caps.Buttons)} / {Number(caps.Values)} / {Number(caps.DataIndices)}";
    private static string FormatFailure(InspectionFailure error) => $"ERROR: {error.Step}" +
        (error.Win32Error is int win32 ? $"; Win32={Number(win32)}" : "") +
        (error.HidStatus is int status ? $"; HID status=0x{unchecked((uint)status):X8}" : "") +
        (error.Step is InspectionStep.OpenInterface or InspectionStep.ReadCollection ? "; zero-access metadata inspection unavailable; no fallback attempted" : "");
}
