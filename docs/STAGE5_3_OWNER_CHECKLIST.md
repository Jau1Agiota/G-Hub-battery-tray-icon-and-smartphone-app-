# Stage 5.3 — owner Windows copy check

All items PENDING. Fetch/checkout `feature/stage5.3-hyperx-confidence-copy`; preserve untracked research files/PublishProfiles. Exit any older companion normally before rebuilding. No safety-marker deletion, forced query, service termination or elevation.

Run the existing tests from the repository root. PowerShell 5.1 block captures relevant output automatically to clipboard, without launching the tray or querying hardware:

```powershell
& {
    Write-Output 'Stage 5.3 Windows Release verification'
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\test.ps1 -Release -CopySummary 2>&1
    $verificationExit = $LASTEXITCODE
    Write-Output "Verification process exit code: $verificationExit"
    $summaryPath = Join-Path $PWD 'test-results\summary.txt'
    if (Test-Path -LiteralPath $summaryPath) { Get-Content -LiteralPath $summaryPath }
} | Out-String -Width 4096 | clip.exe
```

Paste Ctrl+V. There are 37 Windows-specific cases to execute; cloud compiled but did not run them.

Then launch normally using the reviewed build, preserving existing monitoring settings. Existing enabled monitoring still performs its previously approved hardware activity; this copy-only stage adds none.

- When a candidate is present, confirm `Battery estimate: <value>%` remains the main line and `ⓘ Estimate · source under validation` is the secondary line. No assertion of automated/live NGENUITY matching or every-user verification.
- Hover the estimate and secondary line: technical note must still say `Estimate; response source not uniquely verified`. Check accessibility retains uncertainty and headset connectivity unknown; Advanced diagnostics still show candidate/ambiguous state.
- Capture the actual full HyperX card at normal scaling; check 100/125/150% and dark/light theme where practical. New text must fit without ellipsis/clipping; report display DPI/font and mark untested configurations NOT RUN. Do not infer native fit from a portable test.
- Confirm genuine absent/paused/OFF/error messages still appear instead of the neutral note when applicable; do not induce driver/process faults or extra probes.
- Confirm G915/G305 exact/approximate labels, bars, tooltips, startup/Refresh and HyperX cadence remain as Stage 5.2. Close/dismiss/Exit/relaunch unchanged.

Return test summary and screenshot/PASS/FAIL/NOT RUN results. No raw databases, serials, captures or proprietary frontend contents. Stop for review; no automatic merge/release.
