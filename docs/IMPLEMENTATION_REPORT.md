# Stage 3 implementation report (historical)

This records the original Stage 3 delivery. See [Stage 3.1 report](STAGE3_1_REPORT.md) for the current popup and local-verification changes.

Branch: `feature/stage3-windows-tray`. The final commit identifier is provided in the implementation response / Git history.

## Delivered behavior

A windowless Windows Forms application displays the last reported G915 percentage in a native notification-area menu. It uses short-lived read-only Microsoft.Data.Sqlite connections and System.Text.Json parsing, polls asynchronously every 30 seconds, skips overlapping reads, supports manual refresh and cancellation, and holds a per-session single-instance mutex. Missing/bad data is unavailable; prior reports are explicitly cached rather than presented as current. G305 is unsupported and connectivity remains unknown. A dynamically generated color-coded battery icon is recreated only when its value/availability changes. No networking, Android application or WebSocket integration was added.

Core and Windows projects are separate; see ARCHITECTURE.md for models, provider, monitoring service and UI ownership. The only direct runtime package is Microsoft.Data.Sqlite 10.0.14, resolving SQLitePCLRaw 2.1.13. Test dependencies and runtime notices are listed in THIRD_PARTY_NOTICES.md.

## Validation performed in this task

- SDK: 10.0.403 on Linux, satisfying the 10.0.402 / latestPatch SDK pin.
- Solution restore in locked mode: passed.
- Release Windows publish and full Debug Visual Studio solution build: passed, zero warnings/errors.
- Visual Studio support: conventional `.sln`, explicit x64 app target, Project F5 launch profile, portable Debug symbols, optimizations disabled in Debug, and step-by-step Test Explorer/breakpoint guidance in BUILD.md. The IDE itself has not been run on Linux.
- xUnit in each of Release and Debug: **38 passed, 0 failed, 0 skipped**, using synthetic payloads and temporary databases. Includes malformed/missing/empty data, percentage boundaries and invalid types/ranges, UTC handling, warning disagreement, unsupported devices, absent/corrupt/locked/incompatible databases, live committed WAL with an open synthetic writer, unchanged values, cached failures, non-overlapping refresh, caller cancellation, recurring polling, shutdown waiting for in-flight work, and idempotent disposal.
- Self-contained single-file Windows x64 publish: passed; trimming disabled.
- Executable PE header: confirmed AMD64 and Windows GUI subsystem. This is a binary-format check, not evidence of Windows execution.
- Final advisory check: no vulnerable packages reported by the current NuGet sources, including transitive dependencies.
- Distribution archive integrity: passed; includes executable and all third-party notices/licenses.
- Real Windows UI / G HUB / physical peripherals: **not run** in Linux. Follow the exact procedure in BUILD.md. The PowerShell publish wrapper and remote Windows CI have not been claimed as executed by this Linux validation; the equivalent dotnet publish command was executed successfully.

## Artifacts

- `artifacts/win-x64/PeripheralBatteryCompanion.exe` (about 113 MB, self-contained .NET 10.0.14 and native SQLite).
- `artifacts/PeripheralBatteryCompanion-win-x64.zip` (about 45 MiB) includes notices/licenses.
- Executable SHA256: `96830f45b3f76421e0a0cd60efe8384dbfe42c8a0dff2fe32b48e2b2a9aa273d`.
- Test result: `tests/PeripheralBatteryCompanion.Tests/TestResults/stage3.trx` (Release) and `stage3-debug.trx` (Debug).

Artifacts and test outputs are deliberately ignored by Git. CI publishes a downloadable build artifact when its Windows workflow succeeds; no release, default-branch merge, or publication was performed by this task.

## Limitations and next steps

Run BUILD.md's device validation as a normal Windows user, including comparison with G HUB while it is running, repeated/manual refresh, older timestamps, duplicate launch, DPI readability, and exit during refresh. G HUB's local database is undocumented and may change. The executable is unsigned. Native single-file extraction requires a writable per-user temporary directory. Stored values cannot prove current connectivity or freshness, and G305 reporting remains unimplemented.

Original-code MIT licensing remains pending repository-owner approval; no project LICENSE was created. Resolve that before public distribution. No personal databases, user-specific paths, actual device identifiers, secrets or existing monitoring-project sources were added.

## Files changed

Modified: `README.md` (previously only a heading).

Added:

- `.github/workflows/build.yml`
- `.gitignore`
- `Directory.Build.props`
- `PeripheralBatteryCompanion.sln`
- `THIRD_PARTY_NOTICES.md`
- `docs/ARCHITECTURE.md`
- `docs/BUILD.md`
- `docs/IMPLEMENTATION_REPORT.md`
- `docs/RESEARCH.md`
- `docs/licenses/Apache-2.0.txt`
- `docs/licenses/DotNet-Runtime-LICENSE.txt`
- `docs/licenses/DotNet-Runtime-ThirdPartyNotices.txt`
- `docs/licenses/Microsoft-MIT.txt`
- `docs/licenses/WindowsDesktop-LICENSE.txt`
- `global.json`
- `scripts/Publish-Windows.ps1`
- `src/PeripheralBatteryCompanion.Core/BatteryDevice.cs`
- `src/PeripheralBatteryCompanion.Core/BatteryMonitoringService.cs`
- `src/PeripheralBatteryCompanion.Core/GHubSqliteBatteryProvider.cs`
- `src/PeripheralBatteryCompanion.Core/PeripheralBatteryCompanion.Core.csproj`
- `src/PeripheralBatteryCompanion.Core/packages.lock.json`
- `src/PeripheralBatteryCompanion.Windows/BatteryIcon.cs`
- `src/PeripheralBatteryCompanion.Windows/PeripheralBatteryCompanion.Windows.csproj`
- `src/PeripheralBatteryCompanion.Windows/Program.cs`
- `src/PeripheralBatteryCompanion.Windows/Properties/launchSettings.json`
- `src/PeripheralBatteryCompanion.Windows/TrayApplicationContext.cs`
- `src/PeripheralBatteryCompanion.Windows/app.manifest`
- `src/PeripheralBatteryCompanion.Windows/packages.lock.json`
- `tests/PeripheralBatteryCompanion.Tests/MonitoringTests.cs`
- `tests/PeripheralBatteryCompanion.Tests/PeripheralBatteryCompanion.Tests.csproj`
- `tests/PeripheralBatteryCompanion.Tests/ProviderTests.cs`
- `tests/PeripheralBatteryCompanion.Tests/packages.lock.json`
