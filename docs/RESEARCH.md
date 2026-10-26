# Independent research record

These findings were supplied by the repository owner from their own experiments, dated 2026-10-26. This implementation did not inspect or copy existing Logitech battery-monitoring projects or third-party G HUB integrations. No Logitech support or endorsement is claimed.

## Verified SQLite observations

Location: `%LOCALAPPDATA%\LGHUB\settings.db`, with `settings.db-wal` and `settings.db-shm` alongside it.

| Table | Observed columns |
| --- | --- |
| `data` | `_id` INTEGER, `_date_created` datetime, `file` BLOB |
| `snapshots` | `_id` INTEGER, `_date_created` datetime, `uuid` TEXT, `label` TEXT, `file` BLOB |

The `data.file` BLOB held a JSON document with 49 top-level entries, in a single row. `snapshots` was empty. These are installation-specific observations, not a guaranteed schema.

The top-level JSON property `battery/g915/percentage` contained:

```json
{"percentage":45,"time":"2026-10-26T13:41:42Z"}
```

The 45% value was checked against the G HUB graphical interface. A separate `battery/g915/warning` record reported 48%, a `GOOD` level, and a Unix-seconds string timestamp. Its device identifier is deliberately omitted from this public record. The discrepancy establishes that records can differ, so the warning percentage must never replace the verified primary percentage.

The primary timestamp was older than a monitoring observation. It is unknown whether G HUB persists battery data periodically or only on value changes. A readable database is not evidence that G HUB is running, and a stored percentage is not evidence of device connectivity. There is no independently verified connectivity field or data-expiry policy.

## G305

The observed G HUB UI presents qualitative battery information rather than a numerical percentage. Exact labels/internal representations are unverified, and no G305 battery entry was found in the inspected JSON. Stage 3 therefore displays Unknown / Not yet supported, without inferring disconnection. A future provider needs independently collected evidence and comparison with G HUB before implementation.

## Local WebSocket observations (research only)

Observed processes: `lghub_agent.exe`, `lghub_updater.exe`, `lghub_system_tray.exe`, `lghub.exe`, and `lghub_gl.exe`.

| Observed port | Process | Finding |
| --- | --- | --- |
| 9010 | lghub_agent.exe | HTTP 426 for ordinary requests; restricted WebSocket handshake |
| 9180 | lghub_updater.exe | Not investigated further |
| 45654 | lghub_agent.exe | HTTP 426; WebSocket handshake accepted |

Port 9010 rejected an `Origin: http://localhost` handshake with `403 unable to connect from remote origin`. Without an Origin header it returned `400 Bad Request`. Permitted origins/protocol requirements remain unknown. Do not bypass origin restrictions.

Port 45654 accepted a handshake with `101 Switching Protocols` and sent:

```json
{"command":"get-extensions","extensionId":"overwolf","id":"0"}
```

This suggests an Overwolf-related endpoint. It does not establish availability of general device or battery information. Ports may vary between restarts. Stage 3 contains no WebSocket client, port discovery, hardcoded endpoint, or origin workaround.

## Risks and future work

G HUB's database and observed endpoints are undocumented interfaces. Keys, schema, serialization, locking, timestamps and ports can change. The application handles missing/incompatible data as unavailable and does not write to or migrate G HUB's database. Real WAL behavior and primary reading accuracy must still be checked on the user's Windows installation.

Future stages: independently verify G305; then design a securely paired/authenticated LAN API with constrained interfaces and documented firewall behavior; then a Kotlin/Jetpack Compose Android companion for Xiaomi 17T. None is implemented here.

Official technical references: [Microsoft.Data.Sqlite overview](https://learn.microsoft.com/dotnet/standard/data/sqlite/), [async limitations](https://learn.microsoft.com/dotnet/standard/data/sqlite/async), [connection strings](https://learn.microsoft.com/dotnet/standard/data/sqlite/connection-strings), [SQLite read-only WAL requirements](https://sqlite.org/wal.html#read_only_databases), and [NotifyIcon](https://learn.microsoft.com/dotnet/api/system.windows.forms.notifyicon).
