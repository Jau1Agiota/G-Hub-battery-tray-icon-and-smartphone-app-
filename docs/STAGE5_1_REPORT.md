# Stage 5.1 — Logitech bars, precision and startup regression review

2026-10-29. Branch `feature/stage5.1-battery-ui-startup`, based directly on validated Stage 5 `7613ab2bba314ea248b92b0d5518d66160f0c4b5`. No merge, history rewrite or changes to Stage 5/main. Existing unrelated dirty Stage 5 docs were preserved and excluded from this commit; owner Windows JS/PublishProfiles are absent in this cloud checkout and were not touched.

## Owner evidence and root causes

The briefing reports **506/0/0 Windows tests**, Release zero warnings/errors and actual tray readings G915 ~99%, G305 ~90%, HyperX estimate 77% for Stage 5. This is owner evidence, not a new Codex hardware run.

- **Empty Logitech bars:** Stage 5 deliberately returned null bar values when precision was not Percentage. Thus approximate readings could show numbers but empty bars. Both Logitech projections now return a valid current 0–100 value independently of precision; stale/absent/unavailable/unsupported/invalid values still return null. HyperX uses its existing independent estimate path.
- **G915 approximation:** Stage 5 computed exactness as SOC_SUPPORTED **and** discovery useRealSoc != false. A false discovery hint could override an explicit supported battery response. That code path is confirmed; the exact current owner discovery payload was not queried here. Now each battery response governs precision independently: SUPPORTED → Percentage, NOT_SUPPORTED → Approximate, missing/unrecognized → Unknown precision. No inference by model name or another device's flags. Valid values with unknown precision remain available but qualified; failed data remains unavailable.
- **Long labels:** Full provider details previously went straight into narrow card status labels. Compact presentation now uses `G HUB · Precise reading`, `G HUB · Approximate reading`, `G HUB · Precision unknown`, and concise unavailable/absent/stale states. These avoid claiming a physical connection from stale data. Full sanitized status remains in tooltips/accessibility and keyboard settings.
- **Startup:** Inspected actual service/tray code: Start queues PollAsync off the UI; PollAsync awaits RefreshAsync **before** creating PeriodicTimer. The production tray selects 60 seconds. This already meets immediate-first-attempt requirements. No startup/service/timer/HyperX workflow code was changed. New tests demonstrate slow first attempt, handled initial failure, full subsequent 60-second wait, manual refresh without shifting cadence, no overlap, cancellation/disposal without late publication or timer creation.

## Targeted changes

- Core/GHubWebSocketBatteryProvider.cs: per-response precision; discovery hint no longer overrides explicit support; missing/unknown SOC retained separately from data availability. Optional-field validation and only the existing GETs unchanged.
- Core/Presentation/BatteryPanelView.cs: independent numeric fill, typed keyboard precision, concise status labels and defensive suppression of noncurrent values (including explicitly stale/absent state).
- Core/Presentation/BatteryBarGeometry.cs: original proportional inner-width calculation extracted as a pure function, recomputed on every paint. No stored pixel-width cache; zero/narrow/null/invalid bounds safe.
- Windows/BatteryLevelBar.cs: precision-aware Logitech accessibility description, validated values, fill geometry from current control width. HyperX SetEstimate, color and scheduling behavior unchanged; the paint arithmetic it shares is equivalent.
- Windows/BatteryPopupForm.cs: pass numeric fill and precision separately; use compact statuses, retain full details in existing tooltips/accessibility. No redesign, focus, dismissal or Exit changes.
- Tests: GHubGatewayTests.cs precision/order/startup cases; new BatteryBarGeometryTests.cs bounds/current-state cases; PopupLayoutTests.cs real-control paint/resize cases and compact stale-label expectation.

Intentional test expectation updates reflect the new requirements: approximate available readings now fill; SOC_SUPPORTED is precise despite useRealSoc=false; missing SOC is now tested as valid value/unknown precision rather than malformed data. No invalid-percentage, stale/absence, cancellation, correlation or HyperX assertion removed/weakened.

## Verification

Final Release solution build: **PASS, zero warnings/errors**. Full portable suite: **498 passed, 0 failed, 0 skipped**. Baseline is owner-reported 506 Windows passes (477 portable + 29 native). Current declared native count is **36** (seven new 0/1/50/90/99/100/null paint cases): compiled, **NOT RUN on Linux**. Expected combined count is 534 if all run/pass on Windows; this is not a claimed Windows result.

Commands:

```text
dotnet build PeripheralBatteryCompanion.sln -c Release --no-restore --nologo --verbosity quiet
dotnet test tests/PeripheralBatteryCompanion.Tests/PeripheralBatteryCompanion.Tests.csproj -c Release --no-build --no-restore --logger "trx;LogFileName=stage51-final.trx" --verbosity quiet
pwsh -NoLogo -NoProfile -File test.ps1 -Release
```

SDK 10.0.403, existing pinned dependencies. test.ps1 final run: restore/build PASS, portable tests 498/0/0; overall FAIL (exit 1) because WindowsDesktop test host cannot start on Linux. Native cases had zero execution, not passing or skipped results. An initial in-progress runner attempt caught nullable-flow compilation errors after introducing a helper; a NotNullWhen annotation corrected those, and final build is clean. Real clipboard and Windows rendering/device checks NOT RUN. No hardware or gateway connection executed by Codex.

Portable tests verify proportional widths/resize arithmetic, current/invalid states, supported/not-supported/missing/unknown per-device precision and discovery ordering, and injected-clock initial/cadence/manual/cancel/recovery behavior. Native tests call actual GDI paint and inspect fill pixels at larger positive widths; 1% is checked for geometry/render safety, not a brittle anti-alias exact pixel color. They also retain first display/hidden-to-visible popup regression coverage. These compiled tests do not prove physical DPI, shell ordering or actual receiver readings.

Diff/links checks and unchanged-byte audit cover HyperX native/helper/protocol/provider/startup/monitor, SQLite provider, gateway transport, monitoring service, tray startup/cadence and test.ps1/scripts. No dependencies, endpoint/command set, package workflow, user files or artifacts committed. Full Windows acceptance remains owner-pending.

## Owner handoff

Follow [short owner checklist](STAGE5_1_OWNER_CHECKLIST.md). Review/checkout this dedicated branch; run Windows Release verification, then validate first reading without a 60-second wait, next normal poll, bars and per-device precision. No separate probe is needed. Existing HyperX automatic behavior is preserved; launching the application retains its previously approved hardware paths. No G305 raw HID experiment or Stage 3B authorization.

[GitHub branch](https://github.com/Jau1Agiota/G-Hub-battery-tray-icon-and-smartphone-app-/tree/feature/stage5.1-battery-ui-startup). Stage commit is supplied in delivery and listed by `git log --oneline 7613ab2..HEAD`. Stop after push for owner review and Windows/device feedback; do not merge.
