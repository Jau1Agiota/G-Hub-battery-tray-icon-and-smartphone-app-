using System.Globalization;
using System.Text;

namespace PeripheralBatteryCompanion.G305Inspector;

public static class ReportFormatter
{
    public static string Format(InspectionReport report)
    {
        var target = report.EffectiveTarget;
        var text = new StringBuilder();
        text.AppendLine($"Peripheral Battery Companion - Passive {target.Name} HID Capability Inspector");
        text.AppendLine(target.Scope);
        if (target.ReferenceRevision is string revision)
            text.AppendLine($"Reference USB hardware-ID revision: {revision} (supplied, not queried)");
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
                text.AppendLine($@"  Path: \\?\hid#vid_{target.VendorId.ToLowerInvariant()}&pid_{target.ProductId.ToLowerInvariant()}&mi_{target.InterfaceNumber}&{Label(collection).ToLowerInvariant()}#[instance omitted]#[HID class GUID]");
                var item = matches[index];
                if (target == HidInspectionTarget.G305)
                    text.AppendLine(item.ParentGroup is int group ? $"  Anonymous USB interface parent: P{Number(group)} (invocation only)" : "  Anonymous USB interface parent: not established");
                if (item.Failure is not null) { text.AppendLine("  " + FormatFailure(item.Failure)); continue; }
                if (item.Capabilities is not HidCapabilities caps) { text.AppendLine("  ERROR: no capabilities returned"); continue; }
                text.AppendLine($"  Usage page: 0x{caps.UsagePage:X4}; Usage ID: 0x{caps.Usage:X4}");
                text.AppendLine($"  Report bytes: input={Number(caps.InputReportBytes)}, output={Number(caps.OutputReportBytes)}, feature={Number(caps.FeatureReportBytes)}");
                text.AppendLine("  Capability counts (buttons / values / data indices):");
                text.AppendLine($"    Input:   {Counts(caps.Input)}");
                text.AppendLine($"    Output:  {Counts(caps.Output)}");
                text.AppendLine($"    Feature: {Counts(caps.Feature)}");
                text.AppendLine($"  Link collection nodes: {Number(caps.LinkCollectionNodes)}");
                if (target == HidInspectionTarget.G305)
                {
                    if (caps.ReportIds is { } ids)
                    {
                        text.AppendLine("  Capability ReportIDs (not exhaustive descriptor IDs):");
                        AppendIds(text, "Input buttons", ids.Input.Buttons);
                        AppendIds(text, "Input values", ids.Input.Values);
                        AppendIds(text, "Output buttons", ids.Output.Buttons);
                        AppendIds(text, "Output values", ids.Output.Values);
                    }
                    else text.AppendLine("  Capability ReportIDs: not established (not inspected or unexpected usage)");
                }
                var expected = target.ExpectedUsage(collection);
                if (caps.UsagePage != expected.Page || caps.Usage != expected.Usage)
                    text.AppendLine($"  NOTE: differs from supplied expectation {expected.Page:X4}:{expected.Usage:X4}; actual metadata retained.");
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
        if (target == HidInspectionTarget.G305)
        {
            text.AppendLine("Absent capability ReportIDs remain UNKNOWN; ID 0 may mean no ID for that usage.");
            text.AppendLine("Capability metadata is inconclusive for active protocol use; Stage 3B remains NO-GO.");
            text.AppendLine("Parent labels group cached USB MI_02 ancestry only, not a live mouse or wireless slot.");
            if (report.Collections.Select(c => c.ParentGroup).OfType<int>().Distinct().Count() > 1)
                text.AppendLine("Multiple receiver parents: inconclusive; no receiver selected.");
            text.AppendLine("No battery protocol, battery level, connectivity or response ownership is inferred.");
        }
        else text.AppendLine("No battery protocol, battery level, connectivity or physical-parent grouping is inferred.");
        text.AppendLine($"Exit code: {Number(report.ExitCode)}");
        return text.ToString().TrimEnd();
    }

    private static void AppendIds(StringBuilder text, string label, CapabilityReportIds caps)
    {
        var values = caps.Result == ReportIdResult.Available
            ? string.Join(", ", caps.Ids.Select(id => $"0x{id:X2}")) : "not established";
        text.AppendLine($"    {label}: {values}; requested={Number(caps.Requested)}, returned={Number(caps.Returned)}; {caps.Result}" +
            (caps.HidStatus is int status ? $"; HID status=0x{unchecked((uint)status):X8}" : ""));
    }

    private static string Label(ReceiverCollection collection) => $"COL{(int)collection:00}";
    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Counts(ReportCapabilityCounts caps) => $"{Number(caps.Buttons)} / {Number(caps.Values)} / {Number(caps.DataIndices)}";
    private static string FormatFailure(InspectionFailure error) => $"ERROR: {error.Step}" +
        (error.Win32Error is int win32 ? $"; Win32={Number(win32)}" : "") +
        (error.HidStatus is int status ? $"; HID status=0x{unchecked((uint)status):X8}" : "") +
        (error.Step is InspectionStep.OpenInterface or InspectionStep.ReadCollection ? "; zero-access metadata inspection unavailable; no fallback attempted" : "");
}
