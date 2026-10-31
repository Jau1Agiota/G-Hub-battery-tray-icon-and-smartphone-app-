# Stage A2.2B — read-only snapshot foundation

Base: Windows main `d9083a5f6d97849be42b13eb76c27ca6a19556c3`.
Branch: `feature/stage-a2-2b-windows-snapshot-foundation`.
Android A2.1 compatibility inspected at `29871c38ae5c69f8182d7d5f125910a9e3ed291c`;
Android files were not modified. Stage A2.2A reports/schema/example were available
and reviewed before coding. Existing recorded baseline: Stage5.3 report records508
portable passes and37 native cases compiled; that full baseline was not rerun here.

## Scope and mapping decisions

`BatterySnapshotProjection.Build` is a pure function of already-cached immutable
records plus explicit metadata. No provider, delegate, clock, scheduler, native
transport, database connection, UI object or networking client enters its API.
It does not subscribe to production events or change any tray code. A later stage
must own coherent capture/publication of those inputs. No thread/timer/listener is
started, and loading this library does not query a device.

Before implementation, the A2.2A shape was checked against actual models. The exact
schema and illustrative example are preserved byte-for-byte under `docs/contracts/`;
there is no v1 schema revision in this stage. Conservative mapping resolves missing
fields rather than introducing unsupported claims:

| Cached evidence | v1 projection |
| --- | --- |
| G915/G305 gateway numeric0..100, current and not absent | AVAILABLE; preserve response-driven PRECISE/APPROXIMATE/UNKNOWN; GHUB_GATEWAY and corresponding SOC support; runtime identity correlation is ATTRIBUTED, not proof of physical accuracy |
| Missing/null/invalid number | Null current; UNKNOWN confidence; no replacement0%, last-known may remain separate |
| G HUB reports ABSENT | ABSENT_REPORTED; DEVICE_ABSENT; never current cached charge |
| Gateway failure without cache | UNAVAILABLE / SOURCE_UNAVAILABLE; not a fabricated device disconnect |
| Failure with cached reading | Null current, STALE availability/freshness; lastKnown retains percentage, source, precision and original receipt |
| Explicit unsupported model | NOT_SUPPORTED / UNSUPPORTED; never inferred from model name when real gateway evidence exists |
| HyperX valid known supervised candidate | ESTIMATE / AMBIGUOUS, regardless of BatteryDevice's legacy default Percentage precision; UNKNOWN headset connectivity |
| HyperX stale/failure | Null current; historical candidate only in lastKnown, still ESTIMATE / AMBIGUOUS |
| HyperX receiver absent | SOURCE_UNAVAILABLE and UNKNOWN headset connectivity: receiver absence is not proof the headset is absent/off |
| Multiple HyperX receivers | AMBIGUOUS_TARGET; no active current candidate |
| Unknown/unrecognized source | UNKNOWN source/confidence; no free-form source string exported |

Windows has no typed per-reading ERROR availability. v1 conveys source error using
UNAVAILABLE (or STALE with cache) plus SOURCE_UNAVAILABLE. It does not parse Status
strings into operational claims. Detailed monitoring-off/safety-stop classification
requires separately supplied typed aggregate evidence in a later stage; this pure
builder deliberately does not guess these states from UI text. The schema's existing
reason vocabulary is unchanged. It has no dedicated receiver-absence field, hence
the conservative HyperX mapping above; more detail would require owner schema review.

Freshness is UNKNOWN for otherwise-current input; no age-based stale threshold is
introduced. Explicit invalidation/failure/retained cache yields STALE. RECENT_READ is
not inferred from percentage stability or snapshot creation time.

### Timestamp provenance

- Gateway BatteryDevice.LastReported is a local response receipt: maps to readAt,
  observedAt stays null. HyperX candidate LastReported likewise maps only to readAt.
- Legacy SQLite current LastReported is the validated stored report time; it may
  become observedAt. Current DatabaseAccessedAt may become readAt.
- Legacy source attribution requires the caller's explicit LegacySqlite origin
  context (G915 only); it is never inferred from a device name/status. The normal
  default is Gateway, and its actual Source tag must match the known gateway.
- A stored last-known report has no reliable matching SELECT receipt in current
  models. Its observedAt survives with explicit LegacySqlite context, readAt=null;
  do not borrow LastSuccessfulDatabaseAccess or today's SELECT time.
- capturedAt, serverInstanceId and revision are explicit caller-supplied metadata,
  not generated during projection/serialization. They describe cache publication,
  not device measurements. UTC serialization uses Z. Synthetic fixtures reject
  all timestamps, including metadata and lastKnown times.

## Serialization, bounds and privacy

`BatterySnapshotV1` uses immutable records/ImmutableArray and fixed enums/IDs;
`BatterySnapshotJson.Serialize` uses standard System.Text.Json, deterministic camelCase
property order and enum strings, includes JSON nulls and validates version/revision,
canonical three-device order, numeric bounds, known enums, UTC/synthetic timestamps,
current-availability consistency and HyperX evidence semantics before serialization.
Gateway observation timestamps are rejected. Output is limited to64KiB/depth16;
fixed field types and three entries also structurally bound payload construction.
No additional dependency is introduced. No deserialize/network API is provided.

Names, Status, raw source descriptions, paths, runtime device IDs, receiver identifiers,
credentials, raw reports and native diagnostics cannot enter JSON. Only allowlisted
provenance categories survive. Input records remain unchanged. No production monitor,
startup, consent, cadence, SQLite/HID/gateway/native/helper code was edited.

## Verification executed in Codex

.NET SDK10.0.403 from official Microsoft release metadata; SHA512 verified.
Existing packages restored in locked mode. Focused commands:

```text
dotnet restore tests/PeripheralBatteryCompanion.Tests/PeripheralBatteryCompanion.Tests.csproj --locked-mode
dotnet build src/PeripheralBatteryCompanion.Core/PeripheralBatteryCompanion.Core.csproj -c Release --no-restore
dotnet test tests/PeripheralBatteryCompanion.Tests/PeripheralBatteryCompanion.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~BatterySnapshotTests|FullyQualifiedName~GHubGatewayTests"
```

Core Release build: PASS, 0 warnings/errors. Final focused tests:69 passed,
0 failed,0 skipped (26 new snapshot cases,43 existing gateway cases). Gateway tests
use fake injected sockets; no live gateway was contacted. An extra guarded-mapping
review kept receiver absence from asserting headset absence; final tests cover it.

Nine synthetic scenarios generated using the actual final projection/serializer
were independently validated against the unchanged draft2020-12 schema, including
UUID/date-time format checking, using preinstalled Python jsonschema outside the
repository. Cases: empty, available, unknown precision/zero, error cache, absent,
unsupported, stored current, stored cache, synthetic. The synthetic output equals
the supplied A2.2A example. This validator is a verification tool, not a new runtime
or NuGet dependency. XUnit additionally checks JSON shape/nulls/determinism/bounds
and rejects false provenance/timestamp/confidence claims.

Full Windows regression/test.ps1, native UI tests, physical peripherals, gateway,
helper, HID, LAN and Android device testing: NOT RUN. No live monitor was launched.
The full owner-owned regression remains required. No security service is implemented
or claimed tested. Diff checks and protected-file/Android preservation checks passed.

## Owner review / next-stage gate

1. Review the mapping table and exact unchanged schema/sample; confirm that null
   observation/receipt times and conservative UNKNOWN states are acceptable.
2. Run `./test.ps1 -Release -CopySummary` on Windows, sharing the actual summary;
   build/tests must not launch the production tray or query hardware. Keep existing
   assertions and safety policy. This stage does not need a new device experiment.
3. Review synthetic JSON for all three IDs, null versus0, per-response G305 precision,
   HyperX estimate/ownership, current versus lastKnown, times and absence/error states.
4. Verify only new snapshot/test/document files changed; monitoring/UI behavior must
   be unchanged. No APK is expected: Android is read-only in this Windows-only stage.
5. Approve separately before A2.2C. Coherent event capture/publication, authenticated
   LAN service, TLS/pairing/keys/firewall and Android client remain unimplemented.
   No new port, refresh route or hardware query is authorized by this foundation.

No merge, release or physical hardware test was performed. Branch publication/draft
PR are for owner review only; no automatic progression to Stage A2.2C.
