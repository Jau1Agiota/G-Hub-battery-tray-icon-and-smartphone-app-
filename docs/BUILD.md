# Build and real-device validation

## Prerequisites

- Windows 11 x64 for execution and tray validation; ordinary user privileges.
- .NET SDK 10.0.402 or a later patch in the 10.0.4xx feature band (`global.json`). This cloud build used 10.0.403.
- Internet access to NuGet for the initial restore. G HUB and Logitech hardware are not required to build or run automated tests.
- G HUB with its existing current-user SQLite database and a G915 for real-device validation.

The runtime is pinned to 10.0.14 for the self-contained Windows build. Update the runtime pin, dependency pins and lockfiles together as part of maintenance, then revalidate. Do not disable package verification, TLS checks or NuGet auditing.

## Visual Studio debugging

1. Install an **up-to-date Visual Studio 2026** and select **.NET desktop development** in Visual Studio Installer. Install SDK 10.0.402 or a later 10.0.4xx patch if it is not already present, and use a Visual Studio servicing release that supports that SDK feature band. Visual Studio 2022 cannot build/debug this .NET 10 target.
2. Use **File → Open → Project/Solution** and select `PeripheralBatteryCompanion.sln`. Restore NuGet packages if prompted. The Windows, Core and Tests projects are all included in the conventional solution.
3. In Solution Explorer, right-click **PeripheralBatteryCompanion.Windows → Set as Startup Project**. Select **Debug** and **x64** in the solution toolbar, then the **Peripheral Battery Companion** Project launch profile. The project targets x64 explicitly; the solution's Any CPU configuration also maps to this x64 application.
4. Exit any already running copy through its tray menu. Press **F5**. The Debug build includes portable PDBs and disabled optimization for application and core code. F5 launches the regular build output under `bin/Debug/net10.0-windows/win-x64/`, not the published single-file artifact. The self-contained/single-file project settings apply packaging on Publish and do not stop Visual Studio from debugging the normal build output.
5. Set breakpoints in `Program.Main` (startup/mutex), `GHubSqliteBatteryProvider.Read` / `Parse` (database/parsing), `BatteryMonitoringService.RefreshAsync` (polling), and `TrayApplicationContext.Display` / `CloseAsync` and `BatteryPopupForm` (UI/shutdown). Left-click the tray icon to open the battery popup and choose Refresh to trigger another read. Right-click opens the separate application menu. Async provider work runs on a thread-pool thread; use the Threads/Tasks windows when useful.
6. In **Debug → Windows → Exception Settings**, optionally break on thrown `Microsoft.Data.Sqlite.SqliteException` or `System.Text.Json.JsonException` to inspect expected errors before they are caught. Missing databases are represented as an unavailable result, not a thrown exception. Do not add real database contents or personal paths to bug reports.
7. Open **Test → Test Explorer**. Run all tests, or right-click a parsing/monitoring test and select Debug. The tests require no G HUB installation or Logitech hardware, so these are the easiest debugging entry points on another Windows development machine.
8. Use the right-click menu's Exit Application command to exercise graceful cancellation/resource disposal. **Shift+F5** forcibly stops the debugger target and is not a graceful-shutdown test. If a new F5 process exits immediately, check for an older copy and the breakpoint at the single-instance guard.

The IDE itself cannot be exercised on this Linux cloud machine. Debug compilation and the test assembly can be checked here; Windows F5/tray behavior must be confirmed locally.

## Windows commands

Run in the existing checkout; no new repository or worktree is needed:

```powershell
dotnet restore PeripheralBatteryCompanion.sln --locked-mode
dotnet test tests/PeripheralBatteryCompanion.Tests -c Release --no-restore --logger "trx;LogFileName=stage3.trx"
dotnet build src/PeripheralBatteryCompanion.Windows -c Release --no-restore
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Publish-Windows.ps1
```

The PowerShell invocation only selects the execution policy for that script process; it does not change machine policy. You may instead invoke the script directly where your existing policy permits it.

Output: `artifacts/win-x64/PeripheralBatteryCompanion.exe` and `artifacts/PeripheralBatteryCompanion-win-x64.zip`. Launch the executable without elevation. No installed .NET runtime is necessary. The executable bundles native SQLite; .NET extracts required native libraries to its per-user temporary bundle directory on launch. That location must be writable. This is separate from G HUB's directory. The approximately 113 MB executable prioritizes a portable self-contained build over download size; the application itself has no server or framework container.

Single-file publishing is enabled, trimming is explicitly disabled, and notices remain external to the executable. An ordinary folder publish is an alternative if local policy disallows native extraction: override with `-p:PublishSingleFile=false` (restore that deployment variant separately before using locked mode), and distribute the entire output folder. Keep notices/licenses in either format. No code signing, installer, auto-start registration, or auto-update is provided.

GitHub Actions builds/tests on Windows and uploads a ZIP from the feature branch when the workflow runs. A workflow file's presence is not evidence that remote CI has passed; inspect its actual run. Original-code licensing still requires owner approval before public release.

## Linux cloud development

The existing checkout can build the core, run tests and cross-publish the Windows executable. EnableWindowsTargeting is already set in the Windows project. If using the SDK installed during this task:

```bash
export PATH=/workspace/.dotnet:$PATH
export DOTNET_CLI_HOME=/workspace/.dotnet-home
export NUGET_PACKAGES=/workspace/.nuget/packages
export DOTNET_CLI_TELEMETRY_OPTOUT=1

dotnet restore PeripheralBatteryCompanion.sln --locked-mode
dotnet test tests/PeripheralBatteryCompanion.Tests -c Release --no-restore --logger 'trx;LogFileName=stage3.trx'
dotnet build src/PeripheralBatteryCompanion.Windows -c Release --no-restore
dotnet publish src/PeripheralBatteryCompanion.Windows -c Release \
  -p:RestoreLockedMode=true -p:DebugType=None -o artifacts/win-x64
```

The cloud sandbox may require tool approval for the SDK's system named mutexes. No Windows UI can be launched on Linux. No local database files, credentials, services or environment secrets are needed for synthetic testing.

## Exact Windows physical-device validation

Use the current [Stage 3.1 manual Windows checklist](TESTING.md#manual-windows-integration-checklist), including comparison with G HUB, left-click toggle/dismissal, right-click lifecycle controls, DPI/multiple monitors, hidden monitoring, cached/unavailable data and clean exit.

For full local verification and a clipboard-ready summary run `.\test.ps1 -Release -CopySummary`. The runner does not validate native tray behavior or real G915 accuracy. Record those results separately. Never rename/delete/copy the live database or its WAL/SHM files to simulate failure.

## Stage 4.0F helper packaging/debugging

Normal solution builds copy the self-contained production helper into the Windows output's `hyperx-helper/` directory for F5. Publishing the Windows project explicitly publishes that component, and the release PowerShell publisher validates dependencies/files with `scripts/Test-HyperXArtifact.ps1` before ZIP creation. Distribute the entire ZIP/folder; a lone tray EXE lacks the helper and keeps queries disabled. No diagnostic POC executable is launched or shipped as the production worker.

Use breakpoints in `HyperXHelperTransport`/`HyperXBatteryProvider` for supervision/state and attach to the child only in a separately approved native test. Merely opening/debugging the app, Refresh and opting in must not send a battery request (opt-in runs only no-HID simulations). Runtime budgets can expire while a debugger is paused; do not disable them to force hardware success. Review [lifetime, UI cancellation and the physical gate](HYPERX_NATIVE_TRANSPORT.md) before any Query click.
