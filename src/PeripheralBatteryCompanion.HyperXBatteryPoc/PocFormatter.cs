using System.Text;

namespace PeripheralBatteryCompanion.HyperXBatteryPoc;

public static class PocFormatter
{
    public static string Preview(bool previewOnly = true, bool coexistence = false)
    {
        var text = new StringBuilder();
        text.AppendLine("HyperX Cloud III Wireless - Stage 4.0C/4.0D isolated proof of concept");
        text.AppendLine("Target: 03F0:05B7 / MI_03 / COL01 / FF13:0001; input=62, output=62, feature=0");
        text.AppendLine("Exact proposed output: 62 bytes including report ID; byte 0=66, byte 1=89, bytes 2-61=00.");
        text.AppendLine("Payload: " + Hex(HyperXStatusProtocol.CreateStatusRequest()));
        text.AppendLine("Owner verified the full zero tail and the POC wire transfer; coexistence remains experimental.");
        text.AppendLine("Transport: overlapped IOCTL_HID_SET_OUTPUT_REPORT (0x000B0195), not WriteFile.");
        text.AppendLine("Owner-verified USB reference: 21/09, wValue=0266, wIndex=0003, wLength=003E (62).");
        text.AppendLine("One output submission and one interrupt input read; shared READ handle; 3-second exchange deadline.");
        text.AppendLine("No feature commands, initializers, retries, polling, settings changes or provider integration.");
        text.AppendLine(coexistence
            ? "ACTIVE COEXISTENCE: NGENUITY stays open; risk acknowledged: battery reporting may be temporarily disrupted."
            : "Close NGENUITY AND its helper manually before a normal ReadBattery attempt.");
        text.AppendLine(previewOnly
            ? "Payload preview does not open any HID handle or send/receive a report."
            : "ACTIVE opt-in requested: after gates, at most one output and one input will be attempted.");
        return text.ToString().TrimEnd();
    }

    public static string Format(BatteryAttemptResult result, bool coexistence = false)
    {
        var text = new StringBuilder();
        text.AppendLine("HyperX Cloud III Wireless - one-shot battery candidate result");
        text.AppendLine($"Mode: {(coexistence ? "ActiveCoexistence" : "ReadBattery")}");
        text.AppendLine($"Status: {(coexistence && result.Status == AttemptStatus.CandidateReceived ? "CandidateObservedAmbiguous" : result.Status.ToString())}");
        if (coexistence) text.AppendLine("Response ownership: ambiguous, never uniquely correlated. NGENUITY may issue the same request or receive/compete for input. No general coexistence success inferred.");
        if (result.Status == AttemptStatus.UnexpectedFailure)
            text.AppendLine("Request outcome unknown; do not infer non-delivery or retry.");
        else text.AppendLine($"Request submission attempted: {result.RequestAttempted}; output operation completed: {result.OutputCompleted}");
        if (result.Win32Error is int error) text.AppendLine($"Windows API error: {result.ErrorStep}; code={error}");
        if (result.HidStatus is int status) text.AppendLine($"HID status: {result.ErrorStep}; 0x{unchecked((uint)status):X8}");
        if (result.Rejection != ResponseRejection.None) text.AppendLine($"Response rejected: {result.Rejection} (no candidate interpreted)");
        if (result.Status == AttemptStatus.CandidateReceived && result.ValidatedResponse is byte[] report &&
            HyperXStatusProtocol.Validate(report, out var percentage) == ResponseRejection.None && percentage == result.CandidatePercentage)
        {
            text.AppendLine($"Candidate battery percentage: {percentage}% (not yet independently validated for this reader)");
            text.AppendLine("Validated 62-byte response: " + Hex(report));
            text.AppendLine("Bytes 2, 3 and remaining payload fields are uninterpreted.");
        }
        text.AppendLine("No automatic retry. A timeout/error does not prove that the device received nothing.");
        text.AppendLine(coexistence
            ? "Matching NGENUITY percentage is consistency only, not proof of response ownership/freshness. If correlation cannot be established, prefer passive metadata/closed-app diagnostics or redesign before production."
            : "Matching header alone cannot prove freshness/correlation; verify the wire transfer and compare with NGENUITY after the test.");
        return text.ToString().TrimEnd();
    }

    public static int ExitCode(AttemptStatus status) => status switch
    {
        AttemptStatus.CandidateReceived => 0,
        AttemptStatus.UnsupportedPlatform or AttemptStatus.Cancelled => 2,
        AttemptStatus.NoReceiver => 3,
        AttemptStatus.Timeout => 4,
        AttemptStatus.AmbiguousReceiver => 5,
        _ => 1
    };
    private static string Hex(byte[] bytes) => string.Join(" ", bytes.Select(value => value.ToString("X2")));
}
