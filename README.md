# Peripheral Battery Companion

An unofficial, independent Windows 11 tray utility that displays the G915 keyboard's last reported battery percentage from Logitech G HUB. It is not affiliated with, endorsed by, or supported by Logitech.

Stage 3 uses C# / .NET 10, Windows Forms and a read-only SQLite connection. It starts without a main window, polls every 30 seconds, and provides a compact tray menu on left or right click with **Refresh** and **Exit**. A per-session mutex prevents duplicate instances. No elevation, account, network access, installer or background service is required.

The icon is green at 51–100%, yellow at 21–50%, red at 0–20%, and gray when unavailable. The menu distinguishes the reported timestamp, successful database access, cached values after failures, and unknown device connectivity. Stored values have no arbitrary expiry. G305 battery reporting is **not yet supported**; no percentage or qualitative condition is invented.

## Build and try

On Windows 11 x64 with the .NET 10.0.402 SDK (or a newer 10.0.4xx patch):

```powershell
dotnet restore PeripheralBatteryCompanion.slnx --locked-mode
dotnet test tests/PeripheralBatteryCompanion.Tests -c Release --no-restore
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Publish-Windows.ps1
```

Run `artifacts/win-x64/PeripheralBatteryCompanion.exe` as your normal Windows user. The self-contained executable includes .NET and SQLite; keep the accompanying notices and `licenses` directory when sharing it. It is unsigned and may trigger Windows download/reputation prompts. See [build and device validation instructions](docs/BUILD.md).

The Windows project can be cross-compiled on Linux, and the core tests run on Linux without hardware. Windows notification-area behavior and real G HUB database access require a Windows PC. No real-device validation is claimed by this repository's synthetic tests.

## Project guide

- [Architecture and decisions](docs/ARCHITECTURE.md)
- [Independent research and compatibility risks](docs/RESEARCH.md)
- [Build, publishing, and exact physical-device checks](docs/BUILD.md)
- [Third-party dependencies and notices](THIRD_PARTY_NOTICES.md)

There is no Android app, LAN API, WebSocket integration, startup registration, or settings window in this milestone. G HUB's database is an undocumented interface that may change between releases.

## Licensing and privacy

MIT is proposed for original project code, **pending repository-owner approval**. No project `LICENSE` has been added, and no license grant for original code is implied. Resolve this before public distribution. Dependency licenses are separate and included in the notices.

The app reads only the local battery entry. It sends no telemetry, opens no network listeners, and writes no logs or database copies. Do not contribute real G HUB databases, personal paths, device identifiers, or secrets. Tests use synthetic JSON and temporary databases only.
