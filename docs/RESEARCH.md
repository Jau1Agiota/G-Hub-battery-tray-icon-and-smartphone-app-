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

## Stage 4.0A: owner-supplied receiver findings

The owner supplied candidate Logitech receiver `VID_046D / PID_C53F`, USB interface `MI_02`, with vendor-defined collections COL01 `FF00:0001`, COL02 `FF00:0002`, and COL03 `FF00:0004`, sharing the same USB parent interface. The G915 uses a separate `046D:C547` receiver, which the inspector excludes. These are supplied experimental findings, not hardware results produced by the cloud task.

The independent [passive capability inspector](G305_HID_INSPECTOR.md) enumerates those candidate interfaces and reads only preparsed capability metadata with zero-access handles. Its output does not independently verify physical parent grouping, expose instance identifiers, identify a battery field, or establish device connectivity. Nonzero feature/output lengths are descriptor facts, not evidence of a usable battery protocol. No feature requests, report writes, live input reads or production provider are part of Stage 4.0A.

## Stage 4.0B: owner-supplied HyperX findings

The owner supplied HyperX Cloud III Wireless identifiers `VID_03F0 / PID_05B7`, USB hardware-ID revision `4109`, interface `MI_03`, and three collections: COL01 vendor-defined `FF13:0001`, COL02 consumer control `000C:0001`, and COL03 headset `000B:0005`. Windows reportedly listed all three connected/operational. These are supplied hardware-enumeration findings, not capability lengths or telemetry verified by this cloud task.

The [HyperX passive profile](HYPERX_HID_INSPECTOR.md) prioritizes COL01 and reports all three collections through the same zero-access SetupAPI/HID capability reader used for G305. Revision 4109 remains a supplied reference; it is not queried or required in HID paths. The vendor-defined collection is a comparison candidate, not a verified battery endpoint. Input/feature/output lengths do not establish unsolicited telemetry, command safety or decoding. No input capture, audio/driver change, undocumented command or production integration is implemented. Stage 4.0C remains separately reviewed and opt-in after actual Windows results.

## Stage 4.0C: supplied active-protocol evidence (not an executed independent request)

The owner provided working metadata-inspector source and confirmed HyperX COL01 `FF13:0001`, input/output length 62 and feature length 0. Supplied NGENUITY captures showed output prefix `66 89 00 ...` through class SET_REPORT (21/09, wValue 0266, interface 3, length 62), and interrupt input prefixes `66 89 0F 1B 40 00 ...` at 64% and `66 89 0F 27 42 00 ...` at 66%. Byte 4 is a strongly supported battery candidate; bytes 2/3 and off-state/availability semantics remain unknown. Endpoint 84 and address 20 were observations, not stable selectors. No unsolicited report appeared in earlier observation windows, which does not prove one can never occur.

The [isolated one-shot POC](HYPERX_BATTERY_POC.md) sends only the fixed 66/89 + 60-zero packet in an explicitly confirmed mode, with one output, one input and no initializer commands or production provider.

Owner update, 2026-10-27: TShark inspection of frame 9 in the original `hyperx-battery-66.pcap` confirmed the full 62-byte reference request and SET_REPORT fields above. The owner then ran the isolated POC on hardware: `CandidateReceived`, 56%, response `66 89 0E E4 38` followed by zeros. NGENUITY opened afterward also showed 56%. This is owner-reported physical evidence of a matching percentage, not a cloud hardware test or proof of the POC's on-wire transfer. Bytes 2/3, charging/disconnection states and freshness remain unknown. The [USBPcap verification procedure](HYPERX_USBPCAP_VERIFICATION.md) is the next gate before planning production integration; no runtime code changes accompany this record.

## Owner update: POC wire transport verified (2026-10-27)

The owner reports `Hyperx.pcapng` frame 10061: successful control OUT SET_REPORT 21/09, wValue 0266, wIndex 3, wLength 62, exact body 66 89 + 60 zeros. Frame 10062: successful interrupt IN endpoint 84, 62 bytes beginning 66 89 0E E3 38, matching the POC response and 56% candidate. Frame 10063 is a successful zero-length IN URB, not another battery reading. The earlier NGENUITY comparison also showed 56%.

This supersedes earlier pending-wire statements for this observed hardware exchange; it does not validate concurrency, disconnection or long-running resource lifetime. See the [wire record](HYPERX_USBPCAP_VERIFICATION.md) and [coexistence test plan](HYPERX_NGENUITY_COEXISTENCE.md). Coexistence is the next gate. The existing active mode still requires NGENUITY closed; do not bypass that guard to test concurrency. No executable, launcher or production behavior changes accompany this documentation update.

## Stage 4.0D baseline and evidence boundary

The owner reports passive shared GENERIC_READ/READ|WRITE/OVERLAPPED handle opening succeeded with NGENUITY running, without report I/O. This proves access compatibility in that observation, not active response delivery. At the implementation handoff, the separately acknowledged [active coexistence experiment](HYPERX_NGENUITY_COEXISTENCE.md) had not been physically executed; the subsequent owner result is recorded below. No transaction/correlation field is verified; identical NGENUITY polls can make response ownership ambiguous. Production integration remains excluded.

## Owner-reported Stage 4.0D result and production boundary

The Stage 4.0E briefing reports one active coexistence trial on 8ca502e: shared read/overlapped open and capability gate succeeded; output completed; candidate 56%; exit 0; invocation 77.5 ms; NGENUITY stayed open and audio/application were nominal afterward. This supersedes the earlier pending-trial statement for that single observation only. The result remained CandidateObservedAmbiguous: no verified transaction ID, unique ownership or continuous polling reliability. No new hardware query was performed during final integration. Stage 4.0E production metadata/state/UI is implemented with native active querying blocked by the long-running-lifetime gate; see [integration limits](HYPERX_INTEGRATION.md).
