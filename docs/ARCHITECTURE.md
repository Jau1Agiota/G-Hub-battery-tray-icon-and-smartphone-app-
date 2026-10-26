# Architecture

Two application projects keep hardware-independent behavior testable:

- `PeripheralBatteryCompanion.Core` targets `net10.0`. `BatteryDevice` distinguishes availability, reported percentage, and reported UTC timestamp; connectivity is always unknown. `IBatteryProvider` permits future independently verified providers. `GHubSqliteBatteryProvider` resolves the current user's local application data path and reads the verified G915 JSON key. G305 is an explicit unsupported model.
- `PeripheralBatteryCompanion.Windows` targets `net10.0-windows`, x64. `TrayApplicationContext` owns one `NotifyIcon`, a native context menu, an invisible dispatch control, the monitor, and icon resources. The control provides a handle for marshaling updates onto the UI thread; no main form is displayed.
- The xUnit project targets `net10.0` and requires no peripherals or G HUB installation.

## Database and freshness decisions

`%LOCALAPPDATA%\LGHUB\settings.db` is opened with `Mode=ReadOnly`, pooling disabled, and a one-second busy timeout. Connections and commands are disposed immediately after each read. The only SQL is `SELECT file FROM data ORDER BY _id DESC LIMIT 1`. The tested installation had one row; selecting the largest `_id` is an explicit compatibility assumption for future multiple-row installations, not a verified G HUB contract.

SQLite reads the live WAL through its normal locking/coordinating mechanisms. The application does not execute write SQL, change journal mode, create a missing database, copy the database, or explicitly touch/delete WAL/SHM files. Do not use `immutable=1`: this could ignore live WAL updates. SQLite may need existing readable WAL/SHM files and its normal shared-memory coordination; access failures produce an unavailable state. No fallback copies or elevated access are attempted.

The primary source is exactly `battery/g915/percentage`. The warning percentage is ignored. Only integer JSON numbers in 0–100 are accepted. A missing/invalid timestamp does not discard an otherwise valid percentage; it is displayed as unknown. Explicitly zoned timestamps are normalized to UTC and displayed with a full local date, time and offset. Zone-less timestamps are rejected. No stale cutoff is inferred from unchanged data.

A completed SELECT establishes database access, even if its payload has no usable battery record. A valid stored percentage establishes neither a running G HUB process nor device connectivity. `BatteryMonitoringState` keeps current availability, the last valid report, and the last successful SELECT separate. On failure the menu explicitly labels retained data as cached and the icon becomes gray. The cache is memory-only and is discarded on exit.

## Polling and lifetime

A `PeriodicTimer` runs at 30-second intervals after the initial read; manual refresh is immediate when idle. A semaphore skips overlapping refreshes. Microsoft.Data.Sqlite performs synchronous I/O even through its async APIs, so the provider runs the entire database operation via `Task.Run`. UI updates are posted through `BeginInvoke`; no database operation runs on the Windows message thread.

Cancellation prevents subsequent work and suppresses publication of an in-flight result after shutdown. An SQLite call already executing finishes within its normal busy-timeout behavior before cancellation is observed. Exit disables menu commands, cancels polling, awaits the loop and any manual read, unsubscribes updates, hides the icon, and exits the message loop. The context disposes the menu, icon and dispatch handle. Generated HICONs are cloned and released with `DestroyIcon`; the owned icon is replaced only when the displayed percentage/availability changes. Synchronization primitives remain available for safe canceled late refresh calls.

A named `Local` Windows mutex is held for the entire application lifetime. This enforces one process per interactive session, allowing different Windows users/sessions to use their own G HUB data. No framework container, separate configuration file, services, or persistence layer is needed for this milestone.

## Boundaries

The tray UI is Windows-only. Synthetic tests validate parsing, SQLite WAL reads, access errors, caching and lifecycle concurrency; they cannot establish notification-area behavior, live G HUB compatibility, hardware connectivity, or real battery accuracy. Follow the manual validation in BUILD.md before calling the hardware supported.
