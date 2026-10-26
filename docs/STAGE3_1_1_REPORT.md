# Stage 3.1.1: popup sizing and Windows WAL regression fixes

Branch: `feature/stage3-1-1-ui-wal-fixes`, based on Stage 3.1 commit `7cb5b51`. Final commit is supplied in the delivery response / Git history. No default-branch merge or Stage 4 work is included.

## Baseline and root causes

The owner supplied a Windows Release baseline: Restore PASS, Build PASS, 62 tests passed, 1 failed, Clipboard PASS. The failed test was `ProviderTests.ReadsCommittedWalWhileWriterRemainsOpenWithoutChangingData` at its first `File.ReadAllBytes` call. The owner also confirmed the popup renders as a narrow vertical strip. A full baseline run was not repeated here.

**WAL:** `File.ReadAllBytes` opens a file with `FileShare.Read`. On Windows, a new handle's sharing flags must allow the access held by existing handles too. SQLite already has the WAL open for writing, so the test's new handle denies that existing access and fails with IOException before the production provider is exercised. The original sharing assertion worked on Linux, which did not reproduce that Windows-specific conflict. Fixture writer pooling was already disabled, and using-scope order already disposed the writer before directory cleanup; neither needed a workaround.

**UI:** the form used AutoSize/GrowAndShrink with no minimum or preferred client width, deriving its dimensions from an auto-sized, top-docked table. That table's horizontal dock bounds in turn depended on the form client width, creating a circular sizing contract during initial layout/scaling. The column had no explicit percent-width rule. Positioning clamped the resulting window location without establishing readable dimensions, so it could preserve the collapsed width. DPI scaling could amplify the layout problem; working-area clamping alone could not fix it. This is the code-level sizing defect identified from the reported Windows symptom; no native Windows reproduction or trace is claimed on Linux.

## Targeted changes

The form now owns an explicit 360-DIP client width and disables form AutoSize. Its one table column fills that known width; labels wrap within the available content width. After width is established, content height is measured, the actual caption/borders are included, and outer size is capped before edge placement. The minimum width is recomputed for the current working area, preventing both a narrow strip and a previous monitor's minimum from pushing the window offscreen. First show, visible data changes and DPI changes remeasure/reposition it, guarded against recursive SizeChanged callbacks. Existing scrolling handles unusually short work areas. Tray event handlers, dismissal, singleton popup ownership and application exit logic are unchanged.

The WAL test uses a short-lived read stream with `FileShare.ReadWrite | FileShare.Delete` for byte snapshots of its controlled, idle-writer synthetic fixture. It retains every original assertion: WAL existence, primary value, unchanged consecutive device reports, byte-for-byte unchanged WAL and unchanged row count. It additionally compares main database bytes and makes a subsequent commit through the original writer, then verifies the provider sees that new committed value. No sleep/retry loop, checkpoint or SQLite locking workaround is used. This test helper is not a live-database copy/backup strategy.

The production battery reader, monitoring service, `test.ps1`, clipboard functionality, original dependency versions/lockfiles and application lifecycle remain unchanged. A Windows-only test project reuses the existing xUnit/Test SDK versions and is discovered automatically by the unchanged runner and Windows CI. Its new lockfile is included; it introduces no application runtime dependency.

## Regression coverage and verification

- Existing WAL test: same assertions, Windows-compatible observation, extra database/write-continuity checks.
- New sharing-mode test: reproduces the old File.ReadAllBytes failure specifically on Windows, validates shared snapshots preserve an open writer's contents/position/access, then verifies exclusive reopen after disposal. The Windows-only failure assertion was not executed on Linux.
- Nine new dimension/edge cases: 100/125/150/200/250% width policies, ordinary/oversized windows, small working areas, negative monitor coordinates, and fitting size before clamping location.
- Three new Windows Forms tests on STA threads: real readable form/control bounds, long status wrapping/recovery, and reuse at each available screen's edges. They compile on Linux but execute only on Windows. They do not prove real tray click/focus ordering, the app manifest's physical DPI transitions, G HUB behavior or hardware accuracy.
- Locked solution restore including the new Windows test project: **passed**; original lockfiles unchanged.
- Release solution compilation (including the new Windows test assembly): **passed**, zero warnings/errors.
- Focused core regression run: **11 passed, 0 failed, 0 skipped** on Linux. Only the changed WAL test, sharing helper contract, and new dimension cases were run. No complete application suite was rerun here.
- Native Windows tests, full owner Release regression, physical tray/DPI checks and remote CI results: **not observed in this task**.

## Owner verification

On Windows, from this branch:

```powershell
.\test.ps1 -Release -CopySummary
```

The runner should execute both the hardware-independent tests and `PeripheralBatteryCompanion.Windows.Tests`, and copy actual results/failure details. It may briefly show synthetic popup forms during layout tests; it does not start the real tray application or access G HUB. Share the copied summary and complete the updated [manual Windows checklist](TESTING.md#manual-windows-integration-checklist), especially readable first-open/reopen width, 100/150/200% scaling, unavailable-to-valid content changes, all screen edges/multiple monitors, scrolling to actions on constrained screens, and unchanged toggle/dismiss/exit behavior.

The stage remains awaiting owner confirmation on Windows. No claim of full validation or a new published executable is made.

## Files changed

- `PeripheralBatteryCompanion.sln`
- `README.md`
- `docs/ARCHITECTURE.md`
- `docs/STAGE3_1_1_REPORT.md`
- `docs/TESTING.md`
- `src/PeripheralBatteryCompanion.Core/Presentation/PopupPlacement.cs`
- `src/PeripheralBatteryCompanion.Windows/BatteryPopupForm.cs`
- `src/PeripheralBatteryCompanion.Windows/PeripheralBatteryCompanion.Windows.csproj`
- `tests/PeripheralBatteryCompanion.Tests/ProviderTests.cs`
- `tests/PeripheralBatteryCompanion.Tests/TrayPresentationTests.cs`
- `tests/PeripheralBatteryCompanion.Windows.Tests/PeripheralBatteryCompanion.Windows.Tests.csproj`
- `tests/PeripheralBatteryCompanion.Windows.Tests/PopupLayoutTests.cs`
- `tests/PeripheralBatteryCompanion.Windows.Tests/packages.lock.json`
