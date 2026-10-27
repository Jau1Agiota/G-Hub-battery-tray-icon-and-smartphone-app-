# Peripheral Battery Companion

> Stage 4.0H adds optional, default-off automatic monitoring on the supervised helper. The manual-only/process-running restrictions below describe the earlier stage. See [current monitoring behavior](docs/HYPERX_AUTOMATIC_MONITORING.md), including session consent, safety latches, backoff and outstanding Windows validation.

An unofficial, independent Windows 11 tray utility that displays the G915 keyboard's last reported battery percentage from Logitech G HUB. It is not affiliated with, endorsed by, or supported by Logitech.

The app uses C# / .NET 10, Windows Forms and a read-only SQLite connection. It starts without a main window and polls every 30 seconds. **Left-click** toggles a compact battery popup with a horizontal indicator, Refresh and Close. Escape, its caption X, or clicking elsewhere hides it while monitoring continues. **Right-click** opens a separate menu: Open Battery Panel, Refresh, and **Exit Application**. Only that explicit exit command ends the process. A per-session mutex prevents duplicate instances. No elevation, account, network access, installer or background service is required.

The icon is green at 51–100%, yellow at 21–50%, red at 0–20%, and gray when unavailable. The popup prioritizes device names, battery information and the last-reported time. Failures show unavailable data and explicitly label any cached value. Database-access timestamps and unknown connectivity remain separate internally; no unverified connectivity claim appears in the popup. Stored values have no arbitrary expiry. G305 battery reporting is **not yet supported**; no percentage or qualitative condition is invented.

## Debug in Visual Studio

Open `PeripheralBatteryCompanion.sln` in an **up-to-date Visual Studio 2026** with the **.NET desktop development** workload and the pinned .NET 10 SDK. Set `PeripheralBatteryCompanion.Windows` as the startup project, select **Debug / x64** and the **Peripheral Battery Companion** launch profile, then press **F5**. Left-click the tray icon to open the battery popup; no main form is expected. Stop any previously running copy before debugging, because the single-instance guard intentionally closes duplicate launches.

Debug builds use portable symbols and disable optimization. Open **Test → Test Explorer** to run/debug the hardware-independent tests. See [detailed debugging instructions](docs/BUILD.md#visual-studio-debugging), including useful breakpoints and database error handling. Visual Studio 2022 cannot target .NET 10; changing the solution format does not remove that SDK requirement.

## Verify locally

```powershell
.\test.ps1 -Release -CopySummary
```

The runner restores locked dependencies, builds, runs all automated tests, saves bounded logs in gitignored `test-results/`, and copies an actual result/failure summary to the clipboard. It also supports Debug (default), `-BuildOnly`, and `-Verbose`. See [testing instructions and the Windows tray checklist](docs/TESTING.md). Stage 3.1.1 popup-width and WAL-test fixes are awaiting the owner's full Windows regression and native interaction validation. The runner now also discovers the Windows-only form layout tests.

## Passive G305 capability diagnostics

Stage 4.0A adds a separate, metadata-only HID inspector for the candidate `046D:C53F / MI_02 / COL01-COL03` receiver interfaces. It excludes the G915 `046D:C547` receiver and does not add battery reporting to the tray app.

```powershell
.\diagnose-g305.ps1 -CopySummary
```

If execution policy blocks the script:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\diagnose-g305.ps1 -CopySummary
```

The report lists actual usage IDs, report byte lengths and capability counts, omitting instance identifiers from terminal and clipboard. Device handles use zero desired access; there are no HID report read/write commands or broader-access fallbacks. See [scope, errors, and local validation](docs/G305_HID_INSPECTOR.md). Hardware verification remains local.

## HyperX Cloud III Wireless capability diagnostics

Stage 4.0B shares the passive inspector with a fixed `03F0:05B7 / MI_03 / COL01-COL03` HyperX profile. COL01 is inspected first; all matching collections are reported for comparison. The existing G305 launcher retains its original scope.

```powershell
.\diagnose-hyperx.ps1 -CopySummary
```

If policy blocks execution, use `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\diagnose-hyperx.ps1 -CopySummary`. See [HyperX hardware identifiers, report interpretation, and validation](docs/HYPERX_HID_INSPECTOR.md). This is descriptor/capability inspection only; no battery decoding, input sampling, commands, audio changes, or production monitoring integration is added.

## Isolated HyperX battery POC (Stage 4.0C)

A separate experimental project preserves passive inspection as its default. Its first review step is a hardware-free payload preview:

```powershell
.\hyperx-battery-poc.ps1 -PreviewRequest -CopySummary
```

The proposed request is 62 bytes: `66 89` followed by 60 zeros. Confirm that entire tail against the full original USB capture before any active test. An active attempt requires explicit request and NGENUITY-closure flags, validates only HyperX COL01/capabilities, uses a three-second one-shot exchange and never retries or polls. No production battery provider is added. Follow the [protocol evidence, API limitations and staged physical-test checklist](docs/HYPERX_BATTERY_POC.md), one hardware command/result at a time.

## Build and try

On Windows 11 x64 with the .NET 10.0.402 SDK (or a newer 10.0.4xx patch):

```powershell
dotnet restore PeripheralBatteryCompanion.sln --locked-mode
dotnet test tests/PeripheralBatteryCompanion.Tests -c Release --no-restore
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Publish-Windows.ps1
```

Run `artifacts/win-x64/PeripheralBatteryCompanion.exe` as your normal Windows user. The self-contained executable includes .NET and SQLite; keep the accompanying notices and `licenses` directory when sharing it. It is unsigned and may trigger Windows download/reputation prompts. See [build and device validation instructions](docs/BUILD.md).

The Windows project can be cross-compiled on Linux, and the core tests run on Linux without hardware. Windows notification-area behavior and real G HUB database access require a Windows PC. No real-device validation is claimed by this repository's synthetic tests.

## Project guide

- [Architecture and decisions](docs/ARCHITECTURE.md)
- [Independent research and compatibility risks](docs/RESEARCH.md)
- [Build, publishing, and Visual Studio debugging](docs/BUILD.md)
- [Local verification, regression policy, and manual tray checklist](docs/TESTING.md)
- [Third-party dependencies and notices](THIRD_PARTY_NOTICES.md)

There is no Android app, LAN API, WebSocket integration, startup registration, or settings window in this milestone. G HUB's database is an undocumented interface that may change between releases.

## Licensing and privacy

MIT is proposed for original project code, **pending repository-owner approval**. No project `LICENSE` has been added, and no license grant for original code is implied. Resolve this before public distribution. Dependency licenses are separate and included in the notices.

The tray app reads only the local battery entry. It sends no telemetry, opens no network listeners, and writes no logs or database copies. Do not contribute real G HUB databases, personal paths, device identifiers, or secrets. Tests use synthetic JSON and temporary databases only.

## HyperX support (experimental, manual only)

The panel discovers the HyperX receiver through passive metadata and keeps headset connectivity Unknown. Battery queries default off. After explicit session acknowledgement, the app tests a production-owned helper using query-free process simulations; the manual button enables only with successful containment/exit checks and one validated receiver. Missing/failed safety gates keep it disabled. No startup/Refresh/reconnect polling or automatic battery query exists.

The sole manual packet is the owner-verified 62-byte 66/89 request. A result is always **Candidate (Ambiguous)** with observation time/source, never confirmed fresh. Cancel/timeouts are supervised, unsafe/incomplete exit stops further attempts, and the tray never loads native HID transport. The earlier successful one-shot NGENUITY coexistence result does not establish reliable repeated operation.

Distribute the full Windows ZIP, including `hyperx-helper/`; F5 builds also copy the component. Review [the native lifetime decision, IPC, safeguards and one-test approval gate](docs/HYPERX_NATIVE_TRANSPORT.md). No new hardware query was run by Codex. Previous [research/integration evidence](docs/HYPERX_INTEGRATION.md) remains included.
