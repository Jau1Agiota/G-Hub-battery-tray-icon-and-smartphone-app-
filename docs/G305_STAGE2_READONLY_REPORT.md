# G305 Stage 2 — read-only G HUB inspection

## Scope / base

Branch `feature/g305-stage2-readonly-investigation` is based on actual main `2948558` (merged dashboard/startup PR #6). Baseline is the recorded 368 portable passes / 27 Windows-specific cases compiled from that work. No standing policy conflict was found: this stage is a separately invoked read-only investigation, not a provider or monitoring change. The preceding chat PowerShell output was not used as instructions; this implementation follows the supplied briefing.

New project `src/PeripheralBatteryCompanion.G305GHubInspector` contains original database, JSON and CLI code. It references only the existing pinned Microsoft.Data.Sqlite 10.0.14 package; its SQLitePCL dependencies are already pinned in the repository. There is no Core, Windows UI, HID, POC or HyperX helper project dependency and no native HID import. The solution/test project references and locks include the new independent diagnostic; all existing package versions/hashes remain unchanged. The diagnostic is not included in the tray's published dependency graph/ZIP.

## First owner-run command

From repository root in an ordinary Windows terminal:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\diagnose-g305-ghub.ps1 -CopySummary
```

This changes execution policy for that process only, requires the SDK in `global.json`, performs locked restore/Release compilation of the standalone project, then runs it **once**. It does not start the tray application or HyperX helper, poll, elevate, terminate G HUB, inspect live HID reports or change device settings. Script paths resolve relative to the checkout, not the current directory. Without `-CopySummary`, the same sanitized summary prints only to the terminal. Windows PowerShell 5.1 syntax is preserved; PowerShell 7 can use `pwsh.exe` instead.

To copy the command itself:

```powershell
'powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\diagnose-g305-ghub.ps1 -CopySummary' | Set-Clipboard
```

Paste **only the sanitized summary** back to the owner conversation, including nonzero exit status. Do not attach the database, full JSON, raw build logs or private paths. Build/runtime preparation failures omit raw process output entirely rather than relying on partial path redaction. Unexpected managed errors become a fixed failure report. Clipboard failure is identified without erasing a prior diagnostic error. Actual Windows clipboard behavior remains owner-verified.

## SQLite contract

The tool opens LocalApplicationData `LGHUB/settings.db` through SQLite with `Mode=ReadOnly`, `Pooling=false`, `DefaultTimeout=1`, one-second command timeouts. Missing files are not created. There is no raw-byte scan, database/WAL copy, journal-mode change, recovery workaround, application shutdown or retry loop.

A deferred read transaction provides one SQLite snapshot for schema/record inspection. It checks the real `data` table and `_id`/`file` columns, bounding column inspection to 64. It selects `file` from `data ORDER BY _id DESC LIMIT 1`, accepts TEXT/BLOB JSON, checks UTF-8 byte length before returning the value, and checks whether a second row exists without scanning all rows. A connection-local `SQLITE_LIMIT_LENGTH` bound limits SQLite TEXT cast/row allocation; this does not modify any database setting. Returned JSON is limited to 4 MiB. Open/read/schema/empty/parse/limit failures remain distinct and sanitized, with SQLite numeric codes when available.

Only the latest record is parsed. Other rows are explicitly reported as present/not present; their contents, other tables, other files/processes and undocumented storage formats are not searched. This minimizes privacy exposure and matches the app's supported source while documenting its limits. A successful no-finding report does **not** prove G305 battery data is absent everywhere. Normal SQLite WAL/SHM locking/reader coordination may occur, as with the existing G915 provider; no logical data, journal mode or source WAL is written by this tool.

## Structural inspection / privacy

The scanner recursively examines objects/arrays, distinguishing a G305 substring in a **property name** from one in a **string value**. Reference counts describe matching fields, not raw byte substring counts. Model/signature/slot/profile references alone are metadata, not battery telemetry.

For a value to be emitted, an object must have coherent direct `modelId`/`baseModelId` values exactly `g305` (case-insensitive), or a recognized explicit `battery/g305/percentage`, `/level` or `/status` entry must exist. Device identity is not inferred from arbitrary names/signatures, substring aliases or receiver IDs. Scope carries only through recognized battery/telemetry/state branches; settings/profiles/configuration are excluded. A different/conflicting child identity breaks inherited scope. Unknown layouts stay inconclusive rather than guessing.

An integer 0..100 is accepted only from explicit battery-percentage fields or `percentage` inside that recognized battery context. A qualitative level is accepted only from a battery-level field/context and the approved enum `critical`, `low`, `medium`, `high`, `full`, `unknown`. No number is derived from a category. Multiple differing attributed values are suppressed; incomplete/limited inspections suppress all values. These are **cached structural candidates**, not independently verified accuracy, time, physical connectivity or live telemetry. A field named battery is only possible battery/status evidence; its function is not guessed.

Output is limited to 40 finding requests, 120-character sanitized paths, 20,000 traversal nodes and JSON depth 24. Exact allowlisted key names are lowercased; all other keys become `<key>` or `<battery-key>`, even when they contain IDs, hashes, serials or user names. Array indices become `[*]`. Literal slashes in property names are escaped `~1` (JSON-pointer convention), so a slash-bearing key is not confused with nested objects. Repeated redacted fields count toward limits even if output paths coalesce. Only field **types** are printed, including `Boolean` without its raw true/false value. No arbitrary strings, timestamps, serials, instance paths, complete JSON or account values are emitted. Terminal/clipboard report length is bounded below the launcher's 10,000-character gate.

Typical no-evidence report fields:

```text
G305 G HUB read-only inspection v1
Result: Success; SQLite opened: True; supported data(_id,file): True
Rows inspected: 1 (latest only); payload type: blob; latest JSON parsed: True; other rows present: False
Candidate G305 references: <count>; structurally associated battery field: False
Attributed percentage field: not established
Attributed qualitative level: not established
...
```

The real report uses computed counts/booleans, never this example as a default. Exit 0 means the bounded inspection completed, **not that battery support was found**. Exit 1 covers read/schema/parse/limit/unexpected errors; 2 is unsupported platform/arguments; 3 is missing/inaccessible database. SDK/build errors retain their exit code (127 for missing SDK); clipboard failure makes an otherwise successful run nonzero.

## Verification actually performed

- Release solution compilation: PASS, zero warnings/errors; existing Windows-only projects compile but are not executed in Linux.
- New focused synthetic suite: **39 passed, 0 failed, 0 skipped**. Covers model/config-only and G915-only payloads, approved qualitative states, attributable percent, unrecognized/mixed identity, configuration exclusion, ranges/types, nested arrays, malformed/deep/oversized inputs, private keys/values, origin/path escaping, bounded output/work, text/blob SQLite, latest-only/additional-row reporting, unchanged synthetic database bytes, committed WAL with open writer, locks, missing/invalid schema/database, oversized SQLite values, fixed unexpected-error output and dependency/import boundaries.
- The initial limit test failed because duplicate redacted fields coalesced without marking limited output; the implementation was corrected and the assertion retained.
- PowerShell parser: PASS. Linux launcher negative path: expected exit 2, no DB inspection, clipboard explicitly NOT RUN. Native Windows clipboard and real G HUB execution are NOT RUN.
- Full existing regression suite and physical/native Windows checks: NOT RUN in this stage; owner runs `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\test.ps1 -Release -CopySummary` and shares its actual output. No broad repeated suite was substituted for focused checks.
- Diff audit: no production provider, UI, G305 fallback, HyperX native/helper/scheduler/startup/consent, passive inspector, tray packaging or `test.ps1` changes. Solution additions preserve existing AnyCPU/x64 configuration without SDK-generated unrelated x86/BOM churn.

No real G HUB database was inspected here. No real HID API/request, private traffic capture, raw database upload, elevation, G HUB state change, merge or branch deletion occurred.

See [passive HID++ feasibility and the next-stage approval gate](G305_HIDPP_FEASIBILITY.md). Stop after the owner returns this report. Any active discovery/battery POC or production integration requires a new, explicit authorization.
