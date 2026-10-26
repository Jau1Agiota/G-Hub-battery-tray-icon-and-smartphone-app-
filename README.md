# Peripheral Battery Companion

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

The app reads only the local battery entry. It sends no telemetry, opens no network listeners, and writes no logs or database copies. Do not contribute real G HUB databases, personal paths, device identifiers, or secrets. Tests use synthetic JSON and temporary databases only.
