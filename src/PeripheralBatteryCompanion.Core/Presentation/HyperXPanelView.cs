namespace PeripheralBatteryCompanion.Core.Presentation;

public sealed record HyperXPanelView(string ReceiverText, string BatteryText, string ObservedText, string Details,
    bool ExperimentalEnabled, bool CanQuery, string QueryBlocker)
{
    public static HyperXPanelView From(HyperXBatteryProvider provider)
    {
        var state = provider.Current;
        var battery = state.ObservationState switch
        {
            BatteryObservationState.CandidateAmbiguous => $"Battery candidate: {state.Percentage}% (ambiguous)",
            BatteryObservationState.Stale => $"Battery: Stale; last candidate {state.Percentage}% (not current)",
            BatteryObservationState.Unavailable => "Battery: Unavailable",
            // ConfirmedFresh is reserved; this provider never assigns it, even with NGENUITY closed.
            _ => "Battery: Unknown"
        };
        var diagnostic = provider.LastExchangeDiagnostic ?? provider.Diagnostic;
        var details = state.Status + "\nSource: " + state.Source +
            $"\nCurrent action: {provider.Diagnostic.Stage}; last exchange/metadata stage: {diagnostic.Stage}; requested={diagnostic.RequestAttempted}, output completion confirmed={diagnostic.OutputCompleted}; " +
            $"Win32={diagnostic.Win32Error?.ToString() ?? "none"}, HID={diagnostic.HidStatus?.ToString() ?? "none"}; " +
            $"elapsed={diagnostic.ElapsedMilliseconds:F1} ms; completion-safe cleanup={diagnostic.CompletionSafeCleanup}; ownership ambiguous={diagnostic.ResponseOwnershipAmbiguous}." +
            (provider.TransportDiagnostics.Length == 0 ? "" : "\n" + provider.TransportDiagnostics);
        return new($"Receiver: {state.Receiver}; headset connectivity: Unknown", battery,
            $"Last observed: {state.LastReported?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz") ?? "Never"}",
            details, provider.ExperimentalEnabled, provider.CanQuery, provider.QueryBlocker);
    }
}
