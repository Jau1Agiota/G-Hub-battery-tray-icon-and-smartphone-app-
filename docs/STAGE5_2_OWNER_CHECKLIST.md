# Stage 5.2 — owner HyperX startup acceptance

All checks PENDING. Fetch/checkout `feature/stage5.2-hyperx-startup-refresh`, preserve unrelated JS/PublishProfiles and safety stops, and close the previous companion through **Exit Application** before replacing its build. No force-kill/elevation/driver change. Use the existing Visual Studio startup project/build workflow.

Run deterministic Windows verification first. PowerShell 5.1, repository root; this block copies complete relevant output to clipboard without launching the tray/querying hardware:

```powershell
& {
    Write-Output 'Stage 5.2 Windows Release verification'
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\test.ps1 -Release -CopySummary 2>&1
    $verificationExit = $LASTEXITCODE
    Write-Output "Verification process exit code: $verificationExit"
    $summaryPath = Join-Path $PWD 'test-results\summary.txt'
    if (Test-Path -LiteralPath $summaryPath) { Get-Content -LiteralPath $summaryPath }
} | Out-String -Width 4096 | clip.exe
```

Paste Ctrl+V. Windows cases must actually execute; Codex compiled 36 of them but ran none. The portable suite passed 506 cases; those are not the prior owner baseline's 534 combined cases.

Then launch normally after reviewing the changed read timing. Existing enabled HyperX settings now permit one first battery attempt **after** safe discovery/preflight instead of a full interval wait. The same approved query is used; candidates remain ownership-ambiguous, not confirmed fresh. No standalone POC, raw HID diagnostic, safety-marker deletion or new query command is needed.

| Check | Expected | Result |
|---|---|---|
| Enabled, receiver connected, normal software state | Logitech G915/G305 still read promptly with correct bars/precision. HyperX may initially be Unknown while preparing, then reads promptly once ready rather than waiting the saved 60/120/300 seconds. Record actual latency and readiness/safety status. | PENDING |
| Early main Refresh | When ready, gets a real eligible HyperX candidate. During preflight, remains honestly pending/unavailable and the already-owned first-read flow handles readiness; no duplicate/helper error. | PENDING |
| Normal cadence | After the first attempt, next success check uses the saved completion-relative interval. Refresh is an explicit extra eligible attempt; no catch-up bursts or shortened failure backoff. Record configured interval. | PENDING |
| Receiver absent at launch / later connected normally | Unknown/unavailable without fabricated percentage. No native query while missing; first eligible readiness gets one read at an existing state wakeup. Later reconnect after an established session retains normal interval behavior. | PENDING |
| OFF preference | Relaunch OFF and use main Refresh: no automatic enablement, helper preflight or battery query; metadata-only. Explicit ON before a first session attempt permits one first eligible read. Do not clear settings or markers. | PENDING |
| Suspended/backoff/safety stop | Refresh cannot resume, shorten backoff or clear a stop. Normal Resume retains full-interval scheduling; unsafe preparation/cleanup requires owner review, not repeated probing. Use deterministic tests for forced fault injection. | PENDING |
| Dismiss/Exit during readiness or read | Popup close/dismiss hides without ending monitoring. Explicit Exit stays responsive, cancels/joins work, no late update/helper left unexplained. Relaunch obeys durable safety state; never erase it to force a test. | PENDING |
| Regression | HyperX ambiguity/audio/NGENUITY behavior unchanged; Logitech gateway/poll/precision/bar/status unchanged; left-click toggle/right-click management/Exit and layout unchanged. | PENDING |

Return test summary and PASS/FAIL/NOT RUN items with device state, interval, trigger and elapsed time. Do not attach private DB, serials, packet captures or proprietary frontend dumps. No claim of physical fix until results are reviewed; no automatic merge/release.
