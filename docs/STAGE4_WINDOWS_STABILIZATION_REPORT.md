# Stage 4 — Windows stabilization implementation report

2026-10-28. Implementation and Linux-feasible verification complete; **Windows/device acceptance pending**. No physical HID request, real G HUB database access, G HUB/DSX termination, elevation, driver change or permission expansion. G305 research remains paused, Stage 3B NO-GO. Native Android is required Stage 5 after Windows acceptance, not implemented here.

## Inventory and exact base

Clean starting tree at `773cae5` on feature/g305-stage3a4-route-evidence-audit, verified via fetch. Created `feature/stage4-windows-stabilization` from that exact commit. Prior G305 research commits remain ancestors; no merge/rebase/rewrite/deletion. Solution has Core, WinForms tray, shared passive metadata, standalone G305 SQLite/HID inspectors, isolated HyperX POC, helper protocol/native/helper projects, portable tests and Windows-only tests. global.json selects SDK 10.0.402/latestPatch (cloud 10.0.403); runtime/package locks remain unchanged.

Reviewed AGENTS policy, asset dashboard/startup report, existing test workflow, completed G305 research, provider/service/presentation, popup/tray lifecycle and HyperX supervisor/process tests. Existing G915 path is a read-only latest-row SQLite cache reader, not live telemetry. G305 is unsupported/Unknown. HyperX has owner-reported historical one-shot/USB capture/NGENUITY matching evidence, but response freshness/ownership and sustained Windows coexistence are not established by those reports or cloud tests. The existing default-ON/120 startup policy is preserved, not new permission to execute hardware during this task.

## Baseline before edits

Commands (Linux, DOTNET_CLI_HOME=/workspace/.dotnet-home, NUGET_PACKAGES=/workspace/.nuget/packages, /workspace/.dotnet on PATH):

```text
dotnet build PeripheralBatteryCompanion.sln -c Release --no-restore --nologo --verbosity quiet
dotnet test tests/PeripheralBatteryCompanion.Tests/PeripheralBatteryCompanion.Tests.csproj -c Release --no-build --no-restore --logger trx;LogFileName=stage4-baseline.trx --verbosity quiet
```

Pass the logger string as one quoted argument in a shell. Build PASS, **0 warnings / 0 errors**. Tests **427 passed, 1 failed, 0 skipped (428 total)**. Failure: MonitoringTests.OverlappingManualReadsAreSkippedAndShutdownWaitsForRead at line 67, expected OperationCanceledException after requested shutdown but read completed normally. Assertions were preserved. No Windows-native tests executed on Linux.

## Targeted changes and reasoning

1. **Shutdown publication race:** CancelAsync marks its source cancelled immediately but linked cancellation callbacks can remain queued. A synchronous/uncancellable provider read could finish before its linked token observed shutdown and publish a late result. BatteryMonitoringService now checks shutdown and caller source tokens directly after the read, before publishing. The originally failing assertion passes; no sleep/retry or test weakening.
2. **Bounded G915 input:** Previously ExecuteScalar/Parse could materialize/parse arbitrarily large JSON values. Provider now uses a connection-local SQLITE_LIMIT_LENGTH allocation bound and 4 MiB payload budget, consistent with the existing standalone diagnostic. Oversized data is Unavailable; synthetic oversized-row then valid-update recovery passes while its original writer remains usable. This intentionally rejects unusually large legitimate settings blobs too; owner compatibility check remains required. No DB setting/journal write/copy, extra polling or new dependency. SQLite reader WAL/SHM coordination remains normal.
3. **Time and cache semantics:** Future-dated source timestamps now remain unknown (clock difference cannot prove freshness); valid old times are retained. No arbitrary freshness SLA or age cutoff. Known percent means valid **stored value**, not confirmed current hardware. Main G915 card explicitly says “Stored report · freshness unknown”; full source/time reason remains in settings/tooltips. Read failure keeps last-known number separate, neutral current bar/icon; recovery restores stored value. G305 remains Unknown in all views.
4. **HyperX fault coverage:** New simulated child-disposal failure case verifies retained ownership, CompletionSafeCleanup=false and refusal of another query, even with recoverable failures enabled. No supervisor/native/helper/protocol code or permissions changed. Existing tests already cover nonce/schema/length, access errors, no receiver/unplug, timeout, cancellation, forced/nonzero/unknown exits, single flight, backoff, suspension and durable safety stop.
5. **Popup regression coverage:** Current base already fixes the original circular form/child AutoSize dependency: form AutoSize=false, explicit 520×570 contract, DPI recomputation, working-area caps, vertical scrolling and edge placement. No further narrow-strip defect was demonstrated in Linux, so no speculative redesign. Added 96/120/144-DPI three-card/negative-monitor placement coverage and a compiled native test for truthful stored status plus Close→hide→reopen without disposal. Existing long text, themes, font stress, assets, all-screen edge and lifecycle tests retained. Physical DPI/shell behavior is still owner-pending.

## Audit boundaries and remaining risks

| Area | Existing behavior reviewed | Evidence limit |
|---|---|---|
| G915 SQLite | ReadOnly, Pooling=false, 1-second SQLite command/busy budget, entire read Task.Run off UI, latest _id row, null/invalid JSON/percent/schema/locked/corrupt outcomes, committed WAL/concurrent writer and successful later commits | These are synthetic DB tests; actual G HUB refresh/clock/large payload compatibility pending. Cancellation waits for bounded synchronous SQLite work; no hard real-time disk guarantee. |
| HyperX supervision | 5-second child startup/ready budget, 8-second result budget enclosing native 3-second exchange; bounded normal exit 1s, cancel send .25s, exit 1s, termination observation 2s; provider invocation budget 20s. Preflight has two separately bounded no-HID children. Atomic Job membership/limited inheritance, retained startup/unknown exit, finite IPC validation, durable stop/backoff and single flight | Native Windows Job/pipe tests compile only here. Kernel/driver termination can be unconfirmed; retained ownership/latch is refusal, not proof of zero orphan or prompt driver release. |
| Popup vs shutdown | Dismiss/Close hides retained form; monitoring lifetime belongs to tray context. Refresh is async; background displays never Show/Activate. Exit cancels/joins owned services; UI handle teardown follows monitoring stop | Real shell focus ordering and Exit during native work need owner testing. No claim of physical cleanup from fakes. |
| Presentation | G915 stored/cached/unavailable and HyperX ambiguous/stale states; G305 permanently Unknown; current theme/assets/tooltip contracts retained | No actual Windows screenshots, physical high-DPI measurements or audio/coexistence testing performed. |

## Final verification

- Release solution build: **PASS, 0 warnings / 0 errors** (same build command as baseline).
- Full portable suite: **439 passed, 0 failed, 0 skipped** (439 total), logger stage4-final.trx. Includes 10 new WindowsStabilizationTests and 1 new HyperX helper fault case. Baseline failure fixed without removing its assertion.
- Focused provider/monitor/presentation suite during implementation: **112 passed, 0 failed, 0 skipped**; final full suite includes these.
- Windows-only tests: **28 cases compiled, NOT RUN**, not counted as skipped or passed. Includes real STA WinForms and no-HID native process simulations; Windows owner must execute via test.ps1.
- `scripts/Test-VerificationRunner.ps1`: **15 contract checks passed**, synthetic SDK/clipboard/paths/failures only; no real clipboard/hardware.
- `scripts/Verify-DashboardAssets.py`: PASS, canonical source-byte/alpha audit. Not a native screenshot validation.
- Existing `scripts/Publish-Windows.ps1`: PASS, Release self-contained win-x64 publish plus static Test-HyperXArtifact gate. Native/POC dependencies remain isolated from tray, helper packaged, captures/private databases excluded. No executable launched.
- Diff whitespace/docs links and scope checks before commit. test.ps1/clipboard, package scripts, G305 research/tools, HyperX native/helper/protocol/production process supervision/startup scheduling and packet bytecode unchanged. No dependency upgrades.

Changed implementation paths: Core/BatteryMonitoringService.cs, Core/GHubSqliteBatteryProvider.cs, Core/Presentation/BatteryPanelView.cs, Windows/BatteryPopupForm.cs. Tests: new WindowsStabilizationTests.cs; HyperXHelperTests.cs; TrayPresentationTests.cs (assert stored/unknown-freshness semantics instead of empty status); Windows.Tests/PopupLayoutTests.cs. Docs: this report and owner checklist; TESTING.md links/current workflow notes. No unrelated refactoring.

## Owner-testable artifact and delivery

Ignored local artifact: `artifacts/PeripheralBatteryCompanion-stage4-windows-stabilization-win-x64.zip`, **87,391,428 bytes**, SHA-256 `31d2f42ec888f07f5ec89757a114eb364a1cbaceb1e2daa20e853bc47faf3473`. Executable SHA-256 `21cf7d0de7c1feeba61f2e6781ff8a8510603033a90833627c1206336b2acc7c`. This is the existing publish workflow's output copied to a stage-specific filename, not a signed/public release or committed binary. Also buildable locally with the checklist's commands; keep full helper folder/notices together. Artifact corresponds to final source changes; subsequent documentation edits do not alter binaries.

Follow [owner Windows checklist](STAGE4_OWNER_WINDOWS_TEST_CHECKLIST.md), return Release clipboard summary and PASS/FAIL/NOT RUN results. **Owner launch may schedule existing HyperX hardware requests**; Codex did not launch it. No new G305 probe or risky diagnostic is part of the checklist. Acceptance remains pending, not “fully validated.”

[Compare / create draft PR](https://github.com/Jau1Agiota/G-Hub-battery-tray-icon-and-smartphone-app-/compare/main...feature/stage4-windows-stabilization?expand=1). Includes preserved unmerged G305 ancestry as requested; Stage 4 adds only the scoped files above. Stage commit supplied in delivery, available with `git log --oneline 773cae5..HEAD`. No merge, release or deletion. Stop for owner review/Windows feedback; Stage 5 follows acceptance.
