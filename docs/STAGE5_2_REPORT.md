# Stage 5.2 — HyperX first-read readiness and Refresh

2026-10-29. Dedicated branch `feature/stage5.2-hyperx-startup-refresh`, exact base `612e7cb5d804a6c7225c5518978dc9abcc4bdc10`. No changes to Stage 5.1/main, merge or history rewrite. Unrelated dirty/untracked Stage 5 documentation remains preserved and excluded; no JS, PublishProfiles or generated artifacts included. Owner baseline is 534/534 Windows tests and zero-warning Release build, as reported in the corrected briefing. No new physical-device validation by Codex.

## Code-supported root cause

The corrected owner observation is consistent with actual code; manual Refresh previously did **not** retrieve battery:

1. Tray constructor started Logitech independently, fired a HyperX metadata refresh, then queued MonitorHyperXAsync → StartupCoordinator.StartAsync on a worker. The independent metadata call could also overlap discovery; it was not a battery query.
2. Coordinator refreshed metadata, required exactly the validated receiver, persisted a preflight crash marker and awaited the existing real helper PrepareAsync simulation/containment checks. Only then it enabled the provider and configured AutomaticMonitor.
3. Configure assigned next check to readiness time **plus the full saved interval**. Start used normal TickAsync, so it sent no battery request yet. The tray's existing five-second state wakeup eventually reached that due instant (~60 seconds with the owner's saved setting).
4. Both main Refresh actions called RefreshHyperXAsync → AutomaticMonitor.RefreshMetadataAsync, bypassing the coordinator's existing manual query method. They could change receiver status but not produce a battery candidate. The Advanced query action was a different path.

Preflight/receiver/ownership gates are intentional. The initial interval delay and metadata-only Refresh are the behaviors changed by this stage; no safety bypass or new packet is needed.

## Scoped fix and behavior

- **HyperXStartupCoordinator:** one session-local initial-read-pending flag. Once enabled, discovered, preflighted and eligible, request one initial attempt through the existing monitor/provider/durable transport. Consume initial eligibility when the monitor records an attempted check, including a handled refusal/failure. Success does not cause another immediate read; negative results retain normal failure handling. If initially absent/not-ready, eligibility remains pending until the existing state wakeup discovers readiness; no separate retry timer or request queue.
- **HyperXAutomaticMonitor:** initial eligibility uses the same semaphore, provider readiness, suspension, cancellation, process/receiver gates and durable query path. Only the first eligible startup check skips the ordinary interval. Its origin is Scheduled. After an eligible attempt, success schedules completion + saved interval (60/120/300); failures use the same 2/5/15-minute delays and fourth-failure suspension. Initial/manual reads cannot bypass persisted backoff. Normal Tick/reconnect scheduling is unchanged.
- **TrayApplicationContext:** remove the redundant fire-and-forget startup metadata call; the owned coordinator performs discovery. Main Refresh now runs Coordinator.QueryManuallyAsync off the UI thread, parallel to the unchanged Logitech refresh. Startup, Refresh, Advanced manual query and ticks share coordinator/monitor/provider gates. A Refresh overlapping preflight is coalesced/dropped into the already-owned startup work: the same prepared operation performs one first read; no replay after completion. The existing task fields and CloseAsync cancellation/join order own and drain the work; no unowned reader/query task.

### Settings and safety decisions

**Explicit OFF is authoritative.** Main Refresh while OFF is metadata-only, does not enable monitoring, preflight or transmit. Suspension requires explicit Resume; Refresh does not resume it. Resume keeps its existing full-interval wait. Enabling monitoring before any session attempt permits that first eligible read; toggling/reconnecting after an attempt does not reset initial eligibility. Reconnect after an established session keeps the existing full-interval rule; initially missing receiver gets its first read when ready.

Preflight failure, helper missing/containment failure, persistent safety stop, uncertain cleanup or persistence failure remains fail-closed; Refresh does not retry preflight or clear markers. A failed preparation still requires owner-reviewed recovery, not an automatic probe loop. Known NGENUITY state policy, one native flight, exact metadata/target, fixed approved command, three-second native exchange and existing outer helper budgets are unchanged. No protocol/native/helper/transport implementation changes. Candidates remain ambiguous; receiver presence alone is not headset connectivity. Failure/removal/cancellation never fabricates 0% or promotes a late candidate; prior successful value can remain explicitly stale.

Logitech startup/60-second GET cadence, Stage 5.1 precision/bar/status presentation and SQLite provider are byte-identical to base. No live SQLite fallback, G HUB change or G305 HID/Stage 3B work.

## Tests and actual verification

Added Stage52FirstEligibleReadDoesNotWaitForPeriodicInterval **before implementation**: failed on the base with expected one query vs actual zero after StartAsync. The implementation fixes that test. Code inspection (not a claimed native UI test) establishes the previous main Refresh metadata-only wiring.

Updated prior tests that explicitly required an interval before the very first read to assert one initial attempt plus full subsequent cadence. Retained OFF, saved settings, 60/120/300 intervals, failure delay/suspension, persistent marker, cleanup and no-overlap assertions. The late-candidate test now accounts for the first valid startup 50: it proves a late 66 is rejected, last timestamp unchanged, retained 50 stale/unavailable. Persistence failure is injected specifically between successful preflight and first exchange; no assertion weakened to hide an extra query.

New deterministic coverage: prompt first read; Refresh during preflight and before first periodic due; concurrent startup/ticks/manual calls; absent/ambiguous/incompatible then eligible receiver; OFF/suspended modes and Resume; persisted backoff across Refresh/restart; initial query removal with late reply; preflight cancellation/shutdown and repeated failure refusal. Existing helper, scheduler, provider, Logitech gateway and UI geometry tests remain in full suite.

Commands (SDK 10.0.403, existing dependencies/cache):

```text
dotnet test tests/PeripheralBatteryCompanion.Tests/PeripheralBatteryCompanion.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~Stage52FirstEligibleRead" --verbosity quiet
dotnet build PeripheralBatteryCompanion.sln -c Release --no-restore --nologo --verbosity quiet
dotnet test tests/PeripheralBatteryCompanion.Tests/PeripheralBatteryCompanion.Tests.csproj -c Release --no-build --no-restore --logger "trx;LogFileName=stage52-final.trx" --verbosity quiet
```

First command is the recorded pre-fix FAIL. Final build **PASS, zero warnings/errors**. Final portable suite **506 passed, 0 failed, 0 skipped** (eight added cases). **36 Windows-specific cases compiled, NOT RUN** on Linux; expected combined count is 542 if all execute/pass on Windows, not a claimed result. Native clipboard, WinForms/tray ordering, helper/USB/audio behavior and actual first-read latency NOT VERIFIED here. No hardware/gateway access performed by Codex. Diff/links, preserved base and unchanged Logitech/SQLite/native command/helper scope checked before commit. No new packages or runner/script edits.

## Files and owner handoff

Implementation: Core/HyperXStartup.cs, Core/HyperXAutomaticMonitor.cs, Windows/TrayApplicationContext.cs. Tests: HyperXStartupTests.cs. Docs: this report and [owner checklist](STAGE5_2_OWNER_CHECKLIST.md). No unrelated files staged.

[GitHub branch](https://github.com/Jau1Agiota/G-Hub-battery-tray-icon-and-smartphone-app-/tree/feature/stage5.2-hyperx-startup-refresh). Commit reported at delivery and listed by `git log --oneline 612e7cb..HEAD`. No automatic merge or release. Stop for owner Windows/device review: readiness-dependent promptness is implemented/tested with fakes, not yet physically confirmed.
