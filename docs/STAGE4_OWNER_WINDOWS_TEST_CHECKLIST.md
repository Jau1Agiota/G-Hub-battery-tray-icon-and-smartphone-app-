# Stage 4 — owner-only Windows acceptance checklist

All items below are **PENDING** until the owner records PASS / FAIL / NOT RUN. Cloud results are in [the implementation report](STAGE4_WINDOWS_STABILIZATION_REPORT.md). Native Windows UI/process behavior and actual devices were not tested by Codex. Stage 5 is the required native Android phase, **after Windows acceptance**, not implemented here. G305 research stays paused; Stage 3B NO-GO.

## Build and test commands (do not start hardware monitoring)

Use ordinary Windows PowerShell from the repository root, branch `feature/stage4-windows-stabilization`. The SDK in global.json is .NET 10.0.402 with latest-patch roll-forward; .NET 8 alone is insufficient. Keep G HUB and DSX running normally; do not elevate, kill them or modify drivers. Close an older companion through its tray **Exit Application** before replacing files.

Full Release restore/build/tests and clipboard summary:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\test.ps1 -Release -CopySummary
```

To copy that command without executing it:

```powershell
'powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\test.ps1 -Release -CopySummary' | Set-Clipboard
```

This suite includes real Windows layout and **no-HID simulation-only** helper process tests. It does not launch the tray or native battery query. Share the generated summary; native Windows cases must run, not merely compile. Preserve failures/skips. Do not share raw database/capture contents or private paths. The suite's lock/WAL/corruption cases use synthetic temporary databases; never lock, edit, rename or delete the real G HUB database/WAL/SHM for testing.

Build-only alternative (explicitly tests NOT RUN):

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\test.ps1 -Release -BuildOnly -CopySummary
```

```powershell
'powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\test.ps1 -Release -BuildOnly -CopySummary' | Set-Clipboard
```

Build the existing self-contained Windows package and run its static isolation check:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Publish-Windows.ps1
```

```powershell
'powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Publish-Windows.ps1' | Set-Clipboard
```

Output: `artifacts\PeripheralBatteryCompanion-win-x64.zip` and `artifacts\win-x64`. Keep the whole folder together, including `hyperx-helper` and notices/licenses. The cloud package uses the additional stage-specific filename recorded in the report. No signed/public release is published.

## Owner-only launch — existing hardware behavior

**Launching enables the already existing default-ON HyperX monitor (120 seconds unless a valid saved setting differs).** Launch only after owner review and decision to test that approved path. Build/tests above never launch it. No new query mode or G305 diagnostic is required. Preserve explicit OFF, valid 60/120/300 intervals, saved backoff and safety stops. Do not delete/edit a safety marker or clear a safety latch to make tests pass.

One launch from the built folder:

```powershell
Start-Process -FilePath (Join-Path $PWD 'artifacts\win-x64\PeripheralBatteryCompanion.exe')
```

Copy command only:

```powershell
'Start-Process -FilePath (Join-Path $PWD ''artifacts\win-x64\PeripheralBatteryCompanion.exe'')' | Set-Clipboard
```

For an extracted cloud ZIP, run its `PeripheralBatteryCompanion.exe` from the intact extracted folder. Do not start the helper directly or use POC/active-coexistence commands. These are owner-only acceptance actions, not Codex-performed hardware tests.

## Acceptance record

| Item | What to check | Result / notes |
|---|---|---|
| G915 with G HUB running | Shows supported primary stored percentage, never warning fallback. Compare to G HUB as a sanity check. “Stored report · freshness unknown” is visible; Last reported is source time, not last DB access. Connectivity remains unknown. | PENDING |
| G915 Refresh / hidden monitoring | Refresh remains responsive; hide/reopen after at least 65 seconds. Same number/time is valid cached-source evidence, not proof of fresh hardware measurement. Old source times are retained; no arbitrary stale-age SLA. | PENDING |
| G915 missing/error recovery | Synthetic tests cover absent/locked/corrupt/malformed/null/oversized data. If naturally unavailable, current bar/icon neutral; last known number explicitly cached in settings/tooltip, not current. Recovery restores valid stored number. Never manipulate owner DB to cause faults. | PENDING |
| G305 all states | Startup, all three-card views, Refresh, unavailable/recovery, hide/reopen and relaunch always show Unknown/not supported. No G305 HID query, percentage or inferred connection. | PENDING |
| HyperX normal existing path | Respect saved preference/safety state. Query-free preflight then full interval, single flight, no immediate query from popup/Refresh. Estimate remains ambiguous, not confirmed fresh; NGENUITY match is not ownership proof. No audio/G HUB/DSX interference. | PENDING |
| HyperX absence/removal | Through normal existing monitoring only, start with missing dongle if convenient or remove normally. Receiver absence never fabricates 0%; prior estimate becomes stale/unknown, no burst. Reconnect retains full interval/backoff and safety gates. Do not add manual probes. | PENDING |
| HyperX failure/cancellation | Existing Cancel/Resume/OFF controls remain responsive. Review diagnostics and saved 2/5/15-minute failure delays/fourth-failure suspension if naturally triggered; no forced driver/IPC failure or retry loop. Uncertain exit/safety stop blocks further work. | PENDING |
| Tray left/right clicks | Left click toggles one retained popup; right click management menu includes Open Battery Panel, Refresh, Advanced settings and explicit Exit Application. No duplicated window or surprise exit. | PENDING |
| Close/dismiss | Header close, Escape, desktop/other-app click and Alt+Tab hide the panel/settings without ending monitoring; reopen reflects updated cached state. Background updates do not activate it. | PENDING |
| Exit / relaunch | Use explicit Exit, including during ordinary Refresh or an already approved scheduled operation. Cancellation/cleanup remains responsive; icon and process exit, mutex released, relaunch works. Do not force-kill processes for this check. | PENDING |
| Popup sizing | At standard 100% metrics expect 520×570 logical client/outer pixels, not narrow strip. Three full cards and footer actions readable. Long diagnostics accessible in settings/tooltips; no missing number/state. | PENDING |
| DPI / displays | Test 100%, 125%, 150% (nominal 520×570 / 650×713 / 780×855 physical pixels; font/work-area adjustments can grow/cap). Include monitors left/above primary, tray overflow and supported taskbar edges. All content stays onscreen or reachable via vertical scrolling; no horizontal clipping. Mark unavailable setups NOT RUN. | PENDING |
| Themes / accessibility | Dark/light/system/high contrast, text scale, keyboard Tab/Enter/Space/Escape, settings focus/dropdowns, complete product silhouettes and readable state labels. Record actual screenshots/metrics, not arithmetic-test conclusions. | PENDING |
| Idle / resources | Observe at least 15 minutes of normal existing monitoring; record actual duration/interval. Repeated hide/show does not increase windows/GDI resources without bound. Short-lived helpers normally exit; Task Manager shows no unexplained lasting helper after completed operation/Exit. | PENDING |
| Safety stop / orphan suspicion | If helper persists or audio/UI is disrupted, stop via normal Exit, record sanitized diagnostics/timing and pause testing. Do not claim cleanup, erase marker, terminate G HUB/DSX or force another attempt. Unusual kernel/driver termination can remain unconfirmed. | PENDING |

Return the actual test.ps1 clipboard summary, checked results and any failure's trigger/device state/DPI/elapsed time. Do not attach live database contents, instance identifiers, raw report dumps or captures. Windows/device acceptance remains open until owner confirmation; Android Stage 5 follows that acceptance.
