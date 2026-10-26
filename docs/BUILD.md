# Build and real-device validation

## Prerequisites

- Windows 11 x64 for execution and tray validation; ordinary user privileges.
- .NET SDK 10.0.402 or a later patch in the 10.0.4xx feature band (`global.json`). This cloud build used 10.0.403.
- Internet access to NuGet for the initial restore. G HUB and Logitech hardware are not required to build or run automated tests.
- G HUB with its existing current-user SQLite database and a G915 for real-device validation.

The runtime is pinned to 10.0.14 for the self-contained Windows build. Update the runtime pin, dependency pins and lockfiles together as part of maintenance, then revalidate. Do not disable package verification, TLS checks or NuGet auditing.

## Windows commands

Run in the existing checkout; no new repository or worktree is needed:

```powershell
dotnet restore PeripheralBatteryCompanion.slnx --locked-mode
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

dotnet restore PeripheralBatteryCompanion.slnx --locked-mode
dotnet test tests/PeripheralBatteryCompanion.Tests -c Release --no-restore --logger 'trx;LogFileName=stage3.trx'
dotnet build src/PeripheralBatteryCompanion.Windows -c Release --no-restore
dotnet publish src/PeripheralBatteryCompanion.Windows -c Release \
  -p:RestoreLockedMode=true -p:DebugType=None -o artifacts/win-x64
```

The cloud sandbox may require tool approval for the SDK's system named mutexes. No Windows UI can be launched on Linux. No local database files, credentials, services or environment secrets are needed for synthetic testing.

## Exact Windows physical-device validation

Do these steps as the normal Windows user who runs G HUB. Never rename/delete/copy the live database or its WAL/SHM files to test failure handling.

1. Unzip the published distribution to a writable folder. Start G HUB and confirm the G915 battery value in its UI. Launch `PeripheralBatteryCompanion.exe` without administrator privileges. Confirm that there is no main window and exactly one tray icon (it may be in Windows' overflow area).
2. Left-click the tray icon. Confirm that G915 shows the primary battery percentage matching G HUB's reported value, a full local timestamp with offset (or Unknown for absent/invalid timestamps), a database-access timestamp, and connectivity Unknown. An older reported timestamp is allowed. G305 must say Battery Unknown / Not yet supported, regardless of whether the physical mouse is active.
3. Right-click and confirm the same native menu is usable. Select Refresh; confirm that database access advances while the UI stays responsive. Leave it running for at least 65 seconds and reopen the menu; access should advance roughly every 30 seconds even if the percentage and reported timestamp stay unchanged.
4. Launch the executable a second time. Confirm that no additional tray icon remains and only one process runs in this Windows session.
5. While G HUB remains running, compare repeated manual refreshes and the displayed G915 percentage against its UI. This is the required live WAL/read-only compatibility check. The app must not request elevation, stop G HUB, write its settings, or disturb its normal operation. Record observed G HUB version and any mismatch without uploading its database or device identifiers.
6. Turn the keyboard off, then refresh. Confirm the app does not claim it is connected or disconnected based on a stored value. It may retain the same report. Close G HUB normally if desired; the database may still be readable, so closure alone must not force a missing-database status.
7. For a safe missing-database check, use a separate Windows test account with no G HUB database. Launch the executable and confirm a gray icon and a meaningful missing/inaccessible status, without a crash or creating a database. Synthetic tests cover corruption, malformed payloads, schema errors, exclusive locks and empty data without touching real G HUB files.
8. Select Exit during a refresh. Confirm that the icon and process disappear promptly (a busy SQLite read may take about one second). Launch again to confirm the mutex/resources were released. Also check menu readability at your usual Windows DPI setting and on a second monitor if applicable.

Record date, Windows/G HUB versions, displayed primary value versus G HUB, timestamp behavior, refresh/exit/duplicate-instance behavior, and failures. Do not report real hardware as validated until these checks pass. Color boundaries 0/20/21/50/51/100 are synthetic test inputs for the provider; visual color/DPI correctness is a Windows UI check, not an automated claim.
