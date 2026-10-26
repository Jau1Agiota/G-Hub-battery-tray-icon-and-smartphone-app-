# Stage 3.1 delivery: ready for owner validation

Branch: `feature/stage3-1-tray-popup`, based on the merged Stage 3 `origin/main` (68de4ed). Final commit identifier is in the delivery response and Git history. No default-branch merge is performed by this task.

## Implementation

- One compact Windows Forms popup is retained for the application lifetime. Left-click toggles visibility. Escape, deactivation/outside interaction, Close and caption X dismiss it without stopping monitoring or exiting the process. Explicit opening alone activates the form; hidden data updates do not steal focus.
- A separate right-click menu contains Open Battery Panel, Refresh and Exit Application. Opening that menu hides the information panel. The Open command is posted after menu closure, and the explicit exit path waits for monitoring before disposing the tray/popup resources.
- The popup shows the reader-provided G915 percentage, a shared-threshold color battery indicator, last-reported time and concise unavailable/cached messaging. G305 remains Unknown / not yet supported. Unverified connectivity and database-access diagnostics remain out of the everyday popup; existing internal state is preserved.
- Standard working-area geometry, PerMonitorV2 DPI awareness and layout/size constraints support placement near the tray. Native click/focus/DPI behavior is still awaiting Windows validation.
- Small pure visibility, presentation and placement helpers allow regression tests without Windows or hardware. No extra UI framework, G HUB communication method or runtime dependency was introduced.
- Root `test.ps1` implements Debug/default, Release, BuildOnly, Verbose and CopySummary modes, including combinations. It discovers the solution/projects relative to itself, performs locked restores, preserves native command failures, parses fresh TRX counts, rejects zero-executed test runs, produces meaningful failure summaries, and keeps bounded gitignored output under `test-results/`.
- A framework-free fake-command harness tests the runner contract, and lightweight Windows CI reuses the same local runner. The permanent regression policy is recorded in AGENTS.md and TESTING.md.

## Baseline and preservation

Before changes, the existing recorded Stage 3 Release and Debug TRX files each showed **38 passed, 0 failed, 0 skipped**. No pre-existing automated failure was recorded. The owner separately reported the initial application running in Visual Studio. This task did not repeat a complete baseline run.

The SQLite reader, monitoring service, battery models, single-instance entry point, dependency/lock files and original ProviderTests/MonitoringTests are unchanged from origin/main. The new UI calls the same asynchronous monitor and keeps refreshing while hidden. Existing shutdown/cancellation, WAL and parsing coverage is preserved.

## Minimal checks actually performed

- `dotnet build PeripheralBatteryCompanion.sln -c Debug --no-restore`: **passed**, 0 warnings/errors.
- New Stage 3.1 tests only (`FullyQualifiedName~TrayPresentationTests`): **25 passed, 0 failed, 0 skipped**. Covers visibility/exit state, repeated open, context-menu transition, percentage changes, unavailable/cached reports, unsupported mouse, color thresholds, working-area geometry and hidden refresh recovery.
- PowerShell parser checks for `test.ps1` and `scripts/Test-VerificationRunner.ps1`: **passed** using PowerShell 7.5.4 on Linux.
- Runner contract harness: **15 cases passed** using temporary synthetic repositories, fake dotnet commands and a mocked clipboard. Covers actual exit propagation, modes/combined switches, working directory/path spaces, SDK/project errors, failure details, redaction, zero tests and stale/missing/malformed results. This is not an actual .NET regression-suite run or a Windows clipboard test.
- Targeted forced locked restore with older SDK packaging-pack metadata: passed. Effective packaging reference stayed at 10.0.14 under the existing runtime pin, so no dependency/lockfile changes were needed. This is a metadata compatibility check, not an actual run of SDK 10.0.402.
- Original battery/monitoring code/tests compared with origin/main: no changes.
- Whitespace/diff review: passed.

No full Stage 3.1 Release/Debug regression run, real Windows PowerShell 5.1 session, actual Windows clipboard operation, native tray UI/device check, or remote GitHub Actions success is claimed. A new Release executable/ZIP was not produced in this stage; any previous ignored Stage 3 artifacts are not a Stage 3.1 delivery.

## Owner verification

From the checkout in Windows PowerShell:

```powershell
.\test.ps1 -Release -CopySummary
```

The summary reports selected SDK/configuration, Restore/Build/Automated tests PASS/FAIL/NOT RUN, actual passed/failed/skipped/executed counts (or unavailable counts), clipboard status, real failure details, and the overall result/exit code. Share the copied summary, then complete the [manual tray interaction checklist](TESTING.md#manual-windows-integration-checklist). In particular check second/rapid/double clicks, click-outside dismissal, Escape/Close, right-click while visible, hidden monitoring, refresh, unavailable data, G305 wording, multiple-monitor/DPI placement, and Exit Application/resource release.

The optional runner-only check is `.\scripts\Test-VerificationRunner.ps1`. It mocks commands and clipboard and must not be mistaken for full application verification.

## Known limitations

Native Windows message ordering cannot be proven by state tests. Deactivation dismissal waits for mouse release on a short UI timer, and rapid-click/overflow/taskbar behavior needs the actual Windows desktop check. Real G HUB accuracy and live WAL compatibility remain device-specific validation. Actual clipboard availability depends on the user's interactive Windows session. Some Windows 11 installations do not support non-bottom taskbars; mark those checks NOT RUN where unavailable. G305 investigation and all Stage 4 work remain deferred until Stage 3.1 is validated and approved. Original-code MIT licensing remains pending owner approval.

## Files changed

- `.github/workflows/build.yml`
- `.gitignore`
- `AGENTS.md`
- `README.md`
- `docs/ARCHITECTURE.md`
- `docs/BUILD.md`
- `docs/IMPLEMENTATION_REPORT.md`
- `docs/STAGE3_1_REPORT.md`
- `docs/TESTING.md`
- `scripts/Test-VerificationRunner.ps1`
- `src/PeripheralBatteryCompanion.Core/Presentation/BatteryPanelView.cs`
- `src/PeripheralBatteryCompanion.Core/Presentation/PopupPlacement.cs`
- `src/PeripheralBatteryCompanion.Core/Presentation/TrayPanelState.cs`
- `src/PeripheralBatteryCompanion.Windows/BatteryIcon.cs`
- `src/PeripheralBatteryCompanion.Windows/BatteryLevelBar.cs`
- `src/PeripheralBatteryCompanion.Windows/BatteryPopupForm.cs`
- `src/PeripheralBatteryCompanion.Windows/PeripheralBatteryCompanion.Windows.csproj`
- `src/PeripheralBatteryCompanion.Windows/TrayApplicationContext.cs`
- `test.ps1`
- `tests/PeripheralBatteryCompanion.Tests/TrayPresentationTests.cs`
