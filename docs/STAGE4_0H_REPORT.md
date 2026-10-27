# Stage 4.0H implementation report

## Result

Optional HyperX automatic monitoring is implemented for review on `feature/stage4-0h-hyperx-auto-monitoring`, on top of Stage 4.0F `dbcbdeafce2230bafe16502e754df8806fe3794e`. Latest actual main inspected was `4dbf1a30f48063ab181a24c6202ba517809a7ad7`, which does not contain Stage 4.0F. No Stage 4.0G branch/source was present. The public GitHub open-PR dashboard showed zero open PRs; authenticated API access returned Forbidden. This feature therefore includes both the supervised native transport dependency and the automatic-monitoring delta. No branch was merged or deleted.

The [monitoring guide](HYPERX_AUTOMATIC_MONITORING.md) documents controls, timing, privacy, observed hardware evidence and the user-owned Windows checklist. [Stage 4.0F containment design](HYPERX_NATIVE_TRANSPORT.md) remains required context. Its earlier manual-only/process-running restrictions are superseded only where Stage 4.0H explicitly changes them; native lifetime and quarantine gates remain.

## Implementation

- Default-off persisted preference; 60/120/300 seconds, default 120. Consent remains session-only and separate from saved mode. No battery value is persisted. Successful preflight and exact metadata remain required.
- One fake-clock-testable managed scheduler owns manual, automatic, cancellation, reconnect and metadata operations. Five-second metadata wakeups do not transmit battery reports. First/reconnect checks wait a full interval; no queued catch-up. Failed checks use 2/5/15-minute backoff, then suspend after failure four. Resume cannot override unsafe transport state.
- Reuses the exact supervised Stage 4.0F child, Job, IPC, request, native exchange and three-second I/O deadline. A normally exited child with a validated negative result is recoverable; crash, forced/unknown exit, invalid IPC and unconfirmed startup retain the transport latch. No production native HID session/buffer was moved into the tray.
- Production supports NGENUITY observed running or not observed, refusing unknown inspection. Existing isolated POC safety guards are unchanged. All candidates remain ownership/freshness ambiguous, even with NGENUITY not observed.
- Target-specific HID removal notifications immediately disable querying/mark Stale/cancel active work. Metadata fallback handles missing notifications. Idle reconnect revalidates and waits a full interval. Unplug during active native I/O can require diagnostics/restart after a transport latch; this stricter path is deliberate and is not presented as fully automatic recovery.
- Popup adds saved automatic-mode checkbox, interval selector, schedule/last-checked status and Resume. Manual and scheduled checks share candidates, timestamps and sanitized diagnostics. Shutdown drains owned work and prevents late control updates.

## Verification performed in cloud (Linux)

- Baseline: recorded Stage 4.0F 265 portable tests, 10 Windows-specific tests compiled, 15 runner checks.
- Final Release solution compilation: PASS, zero warnings/errors. Eleven Windows-specific tests compiled (including one new real-control test), **not run** on Linux.
- Portable Release automated suite: **299 passed, 0 failed, 0 skipped**. Includes 29 new scheduler/preferences/response-policy cases and five transport recovery/crash/exit cases; all 265 previous cases retained. Final TRX: `tests/PeripheralBatteryCompanion.Tests/TestResults/stage4-0h-complete.trx` (ignored build output).
- Existing PowerShell verification-runner contract checks: **15 passed**; clipboard behavior preserved. No native Windows clipboard verification claimed.
- Debug win-x64 self-contained publish with isolated helper: prepared as `artifacts/stage4-0h/debug-win-x64`; final ZIP `artifacts/PeripheralBatteryCompanion-stage4-0h-debug-win-x64.zip`. Rebuild the committed tree and run the static package gate before distributing. Artifacts are build outputs, not committed source.
- Diff audit: no Stage 4.0H changes to G915 provider, G915 monitor, tray icon, standalone POC entry point, native exchange implementation, diagnostic launchers or `test.ps1`. No captures, real DBs or machine-specific HID paths added.

No Windows process/UI runtime tests, hardware battery query, sustained coexistence check, merge or branch deletion was performed. Do not claim fully validated automatic monitoring until the owner completes the documented supervised Windows checks. Owner-reported matching percentages and post-read process listings do not establish response ownership or polling reliability. A defective driver can delay process termination indefinitely; unconfirmed exit is retained/disabled, never reported as clean success.

## Reproduce the review artifact

From a .NET 10 Windows-capable build environment, without launching the tray/helper:

```powershell
dotnet publish src/PeripheralBatteryCompanion.Windows -c Debug -p:RestoreLockedMode=true -o artifacts/stage4-0h/debug-win-x64
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-HyperXArtifact.ps1 `
  -PublishDirectory .\artifacts\stage4-0h\debug-win-x64 `
  -ParentDependencyFile .\src\PeripheralBatteryCompanion.Windows\bin\Debug\net10.0-windows\win-x64\PeripheralBatteryCompanion.deps.json
Compress-Archive -Path .\artifacts\stage4-0h\debug-win-x64\* -DestinationPath .\artifacts\PeripheralBatteryCompanion-stage4-0h-debug-win-x64.zip -Force
```

Hardware execution remains a separate owner-reviewed step. Use the Windows checklist, not an automatic physical-test script.
