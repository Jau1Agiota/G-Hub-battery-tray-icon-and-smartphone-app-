using System.Diagnostics;
using PeripheralBatteryCompanion.Core;
using PeripheralBatteryCompanion.G305Inspector;

namespace PeripheralBatteryCompanion.Windows;

// Metadata-only; deliberately no dependency on the POC or its active native session.
internal sealed class HyperXReceiverDiscovery : IHyperXReceiverDiscovery
{
    public Task<HyperXDiscoveryResult> DiscoverAsync(CancellationToken token) => Task.Run<HyperXDiscoveryResult>(() =>
    {
        token.ThrowIfCancellationRequested();
        var backend = new WindowsHidMetadataBackend(HidInspectionTarget.HyperX);
        var result = HyperXCollectionRules.Inspect(backend);
        token.ThrowIfCancellationRequested();
        var presence = result.Presence switch
        {
            HyperXMetadataPresence.Detected => ReceiverPresence.Detected,
            HyperXMetadataPresence.Absent => ReceiverPresence.Absent,
            HyperXMetadataPresence.Ambiguous => ReceiverPresence.Ambiguous,
            _ => ReceiverPresence.InspectionFailed
        };
        return new(presence, result.Failure?.Win32Error, result.Failure?.HidStatus);
    }, token);
}

internal sealed class NgenuityPresenceProbe : INgenuityPresenceProbe
{
    public Task<NgenuityPresence> InspectAsync(CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        try
        {
            var processes = Process.GetProcesses();
            try
            {
                foreach (var process in processes)
                {
                    token.ThrowIfCancellationRequested();
                    try { if (process.ProcessName.Contains("ngenuity", StringComparison.OrdinalIgnoreCase)) return NgenuityPresence.Running; }
                    catch (InvalidOperationException) { }
                }
                return NgenuityPresence.NotObserved;
            }
            finally { foreach (var process in processes) process.Dispose(); }
        }
        catch (OperationCanceledException) { throw; }
        catch { return NgenuityPresence.Unknown; }
    }, token);
}
