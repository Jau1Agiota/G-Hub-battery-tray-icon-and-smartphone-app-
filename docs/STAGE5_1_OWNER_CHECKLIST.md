# Stage 5.1 — owner Windows checklist

All items PENDING. Fetch/checkout `feature/stage5.1-battery-ui-startup` in Visual Studio, preserving unrelated untracked JS files and PublishProfiles. No clean/reset, elevation or forced process termination. Use the existing Windows startup project or publish workflow; do not launch old Stage 5 binaries and attribute them to this fix.

First, run from the reviewed checkout in **Windows PowerShell 5.1**. This block captures the complete relevant verification output and summary automatically to clipboard; it does not launch the tray or query hardware:

```powershell
& {
    Write-Output 'Stage 5.1 Windows Release verification'
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\test.ps1 -Release -CopySummary 2>&1
    $verificationExit = $LASTEXITCODE
    Write-Output "Verification process exit code: $verificationExit"
    $summaryPath = Join-Path $PWD 'test-results\summary.txt'
    if (Test-Path -LiteralPath $summaryPath) { Get-Content -LiteralPath $summaryPath }
} | Out-String -Width 4096 | clip.exe
```

Paste Ctrl+V back to the conversation. All **36 Windows-specific cases** must execute to establish native test success; Linux compiled them only. No physical verification is implied by test passes.

Then perform normal application checks, one launch at a time (exit the previous instance via its explicit Exit command). Launching retains previously approved G HUB GETs and existing HyperX scheduling; no new diagnostic/probe is needed.

| Check | Expected | Result |
|---|---|---|
| Start with G HUB running | Tray/UI responsive immediately; initial G915/G305 read without waiting for first 60-second tick. A slow attempt remains bounded by the existing 12-second gateway budget. | PENDING |
| Next poll | After initial attempt completes, normal 60-second cadence; no duplicate startup poll/burst. | PENDING |
| Start when G HUB naturally unavailable | Honest unavailable, no SQLite live fallback; responsive popup/Exit. After owner normally starts G HUB, later normal poll/manual Refresh recovers. Do not alter services/drivers. | PENDING |
| Manual Refresh | Immediate async attempt; no overlap/duplicate reads or reset of the automatic cadence. HyperX Refresh remains metadata-only. | PENDING |
| G915 SOC_SUPPORTED | Valid response shows 99% without tilde/approximate label, almost-full bar, concise Precise reading status. Actual value may differ from baseline. | PENDING |
| G305 SOC_NOT_SUPPORTED | ~90% (approx.) remains qualified, about nine-tenths filled, Approximate reading status. No precise measurement/endurance claim. | PENDING |
| Unknown/stale/unavailable | Unknown precision stays qualified while valid; actual unavailable/absent/invalid/stale has no current filled bar. Prior cached values remain explicitly stale in details. | PENDING |
| Display and resize | First show and hide/reopen have correct fills; labels readable at 100/125/150% where available, full details in tooltip/accessibility/settings. No panel redesign or clipped actions. | PENDING |
| HyperX regression | Existing estimated value/bar, cadence/preferences, helper lifetime, audio and controls unchanged. Do not remove safety stops or run isolated POC queries. | PENDING |
| Close/relaunch | Dismiss hides without stopping monitoring; Exit during startup/Refresh cancels/awaits safely; relaunch starts one immediate read. No late UI update or orphan process. | PENDING |

Report PASS/FAIL/NOT RUN with exact trigger, display and test summary. No raw database, serials, proprietary frontend dump or report capture. G305 raw-HID work remains paused, Stage 3B NO-GO. Stop for review; no automatic merge/release.
