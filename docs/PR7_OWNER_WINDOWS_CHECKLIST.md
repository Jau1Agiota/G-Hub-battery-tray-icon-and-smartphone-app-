# PR #7 — consolidated Windows acceptance gate

**DO NOT MERGE until the owner explicitly approves. Keep PR draft.** This checklist is authoritative for current PR #7; earlier stage checklists are historical (not current instructions to use 360-pixel layout, SQLite live data, unsupported G305, or metadata-only HyperX Refresh). [Audit findings](PR7_CONSOLIDATED_AUDIT.md).

## Source and deterministic tests

1. Exit the old companion via tray **Exit Application**. Preserve untracked JS/PublishProfiles/research files and all safety-stop markers. Fetch the existing `feature/stage5.3-hyperx-confidence-copy` branch without force/reset; verify exact final SHA from delivery/PR. No forced G HUB/DSX/NGENUITY termination, elevation, driver edits, owner database changes or safety-marker deletion.
2. Inspect GitHub PR #7: draft, base main, intended head, CI checks/reviews and no unrelated staged files. Codex cannot read/write PR metadata in this session (Forbidden); do not infer check success from the local report. If necessary, paste [the prepared description](PR7_DESCRIPTION.md) and retain draft manually.
3. Run the following in **Windows PowerShell 5.1** from repository root. It captures the exact commit and existing Release runner output/summary automatically to clipboard. It does not launch the tray or query hardware.

```powershell
& {
    Write-Output 'PR #7 consolidated Windows acceptance'
    & git log -1 --format='%H'
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\test.ps1 -Release -CopySummary 2>&1
    $verificationExit = $LASTEXITCODE
    Write-Output "Verification process exit code: $verificationExit"
    $summaryPath = Join-Path $PWD 'test-results\summary.txt'
    if (Test-Path -LiteralPath $summaryPath) { Get-Content -LiteralPath $summaryPath }
} | Out-String -Width 4096 | clip.exe
```

Paste Ctrl+V. Require Restore/Release Build PASS, zero warnings/errors, **545 passed / 0 failed / 0 skipped**, including **37 executed Windows cases**, at the audited source revision (documentation-only audit adds no tests). A zero-executed Windows host or BuildOnly run is not acceptance. Native tests use STA controls/paint and simulation-only helper modes; they do not approve physical querying. Inspect test-results/latest.log for build details; report errors/skips honestly.

## Owner-only Release package and application

After review/test success, use existing local packaging:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Publish-Windows.ps1
```

Inspect its static isolation PASS and build output, then launch the actual current Release package (keep helper/notices/license files together):

```powershell
Start-Process -FilePath (Join-Path $PWD 'artifacts\win-x64\PeripheralBatteryCompanion.exe')
```

This is an **owner-only physical acceptance action**. Launch issues approved local G HUB GETs and, if enabled and eligible, the existing supervised HyperX request after readiness. It is not a new raw G305 probe or a license/release approval. Do not run old POC/active-coexistence/Stage 3B diagnostics. No binary/public release is published by this checklist; original-code distribution licensing still awaits owner decision.

| Test | Expected / capture | Result |
|---|---|---|
| G915/G305 startup and manual Refresh | Prompt read-only local gateway data, G915 precise only when explicit SOC_SUPPORTED, G305 NOT_SUPPORTED conspicuously ~approximate with proportional fill. Dynamic runtime IDs, no SQLite live fallback. | PENDING |
| Device absent / G HUB normal restart | No query to absent entry, current value Unknown/unavailable and retained value stale, no false 0%; next normal 60-second/manual refresh reconnects without spin/retries. Backend ACTIVE is not independent radio proof. Do not force process/service failure. | PENDING |
| HyperX initial / early Refresh | Once validated receiver/preflight ready, first attempt without arbitrary full-interval wait. Early Refresh ready → eligible read; during preflight remains pending/coalesced, no duplicate helpers. Record actual latency and saved interval (owner uses 60s; valid 120/300/default120 retained). | PENDING |
| HyperX periodic / OFF / pause/backoff | Normal completion-relative cadence, no aggressive retry. Explicit OFF remains metadata-only/no enablement; Refresh does not clear pause/backoff/latch. Resume retains full interval; uncertain exit stops querying. Test faults with fakes, not forced driver/process damage. | PENDING |
| Exact Stage 5.3 wording | Main `Battery estimate: <value>%`; secondary `ⓘ Estimate · source under validation`; hover/accessibility/Advanced retain `Estimate; response source not uniquely verified` or equivalent explicit ownership ambiguity. No automated official-app match claim. | PENDING |
| Native layout / screenshots | Full HyperX card screenshot at normal DPI; additional 125/150% and dark/light/high-contrast as practical. No ellipsis/clipping of new copy, full tooltips, three cards/520×570 nominal dashboard and reachable footer/scroll adaptation. Mark untested setups NOT RUN. | PENDING |
| Tray focus/dismiss / Exit/relaunch | Left-click toggles one retained popup; background updates do not focus it. Escape/close/outside click hide only. Right menu includes Advanced and explicit Exit. Exit during ordinary pending startup/refresh cancels/joins, no late UI update; relaunch obeys preferences/safety state. | PENDING |
| Stability / audio / process lifetime | Observe normal idle/cadence, no bursts, audio/software disruption or unexplained helper after completed work/Exit. On abnormal behavior pause via normal controls/Exit and report sanitized diagnostics; never erase a stop or force another attempt. | PENDING |

Return test summary, exact head, screenshots and PASS/FAIL/NOT RUN table with timings/intervals/triggers. No serials, raw databases, private capture or proprietary dumps. Owner Stage 5.2's 542 passes/device success does not confirm Stage 5.3's 545 or current final head. CI and explicit approval plus this checklist remain merge gates; no automatic merge/release/Android work.
