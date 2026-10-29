# Stage A2.2C — non-listening service core

## Decision and exact boundary

Implement the brief's non-listening fallback, not a production secure LAN service.
No socket/listener, TLS handshake, certificate creation, pairing UI, persistent
credential storage, firewall change, app startup integration or Android networking
is implemented. None of the new classes is instantiated by production tray code.
No monitoring, consent, scheduling, refresh, native transport or UI file changed.

Base: A2.2B `c6971071e541099cf1b8e8cfef0b4ee1d3a1c1dc`, PR8, still open/draft
when audited. Its base is Windows main `d9083a5f6d97849be42b13eb76c27ca6a19556c3`.
This feature branch is `feature/stage-a2-2c-non-listening-service-core`. Its draft
PR targets the A2.2B feature branch so it does not duplicate that open PR's changes.
No dependency merge or history rewrite was performed. Owner may retarget after
reviewing/merging the prerequisite; this stage does not do that automatically.

Android compatibility remains at A2.1 `29871c38ae5c69f8182d7d5f125910a9e3ed291c`.
A2.2A report/schema and A2.2B implementation/PR were read. Schema v1 is byte-identical;
no new wire fields, source claims, age thresholds or accuracy upgrades were added.
Owner-reported A2.2B baseline: Release restore/build PASS,571 Windows tests passed,
0 failures/skips, exit0; native tray/physical checks NOT RUN. That is owner evidence,
not a Windows run performed in this session.

## Lifecycle/cache audit and future integration point

At the inspected base, TrayApplicationContext.OnUpdated/Display retains the latest
BatteryMonitoringState; HyperX updates already marshal through the dispatch control
and DisplayHyperX reads immutable provider.Current. Startup and manual Refresh own
provider activity. CloseAsync cancels/joins those operations before UI teardown.
HyperXPreferencesStore persists monitoring policy and safety stops, not LAN consent
or credentials. Reusing that JSON for secrets or enabling API access would be wrong.

The future adapter should forward already-owned cache notifications into
CachedSnapshotPublisher on the existing cache-update path, before any request could
read the resulting store. It must never subscribe an HTTP request to RefreshAsync,
QueryManuallyAsync, SQLite, gateway, receiver discovery or helper preparation.
The adapter must detach its cache hooks and stop/drain transport/request processing
before disposing the independent publisher/store, preserving existing monitor exit
order. This stage supplies the testable publisher but deliberately does not wire it
into the app. A combined cached view is coherent at publication; observations from
different sources are not simultaneous hardware measurements.

## Components and invariants

- PublishedSnapshotStore atomically replaces one immutable snapshot/UTF8 payload.
  Only explicit producer publication creates a new monotonic revision and capturedAt;
  readAt/observedAt stay unchanged. Caller supplies an anonymous server-instance UUID.
  Concurrent readers see matching JSON/revision/model. Reads are one volatile pointer
  read: no clock, serialization, provider, task, callback or hardware operation.
  Invalid or synthetic:true input cannot replace the last good publication. Terminal
  disposal clears its private cache and refuses further publication.
- CachedSnapshotPublisher serializes explicit Logitech/HyperX cached-input events
  under one gate, retains the other source, and calls the unchanged A2.2B projection.
  Missing sources remain null/unknown; HyperX retains ESTIMATE/AMBIGUOUS and UNKNOWN
  headset connectivity. No source factory, poller, UI object or native API is injected.
  It does not create event subscriptions, start monitors or infer freshness by age.
- ApprovedPhoneRegistry is an IN-MEMORY verifier/authorization boundary only. An
  eventual locally approved enrollment supplies SHA256 of a CSPRNG-generated32-byte
  token and an anonymous phone UUID. It stores copied digests, not raw credentials;
  supports per-phone revoke/rotation, at most16 entries and rejects duplicate hashes.
  It checks a bounded canonical Bearer/base64url credential and uses constant-time
  digest comparison. No credentials are issued, persisted, paired, printed or logged.
  Hash approval is a trusted local API, not proof of a person's approval by itself.
- SnapshotRequestCore is a synchronous in-process response constructor, NOT an HTTP
  server. It starts OFF; only explicit EnableByLocalOwner enables this object, and
  disposal is terminal. Disable/re-enable does not reset quotas. It depends on a
  read-only published-data interface, registry and monotonic clock. It exposes only
  a proposed exact GET /v1/battery-snapshot operation; no queries, request bodies,
  refresh, commands or remote enrollment routes. Malformed/unauthorized/disabled/
  canceled requests do not read the cache. It reuses the exact published byte array.
- Fixed errors never echo request values, exceptions, credentials or paths. Request
  ToString is redacted; response metadata specifies application/json and no-store.
  JSON is bounded to the existing64KiB/depth16 contract; success cannot return a
  synthetic:true payload. An empty/closed/failing store returns a generic503.
- Registry authorization holds a short synchronous gate through response construction;
  revocation cannot finish mid-construction. Once revocation returns, future requests
  are denied. Stop similarly drains synchronous construction and refuses re-enable.
  Neither can unsend a response already constructed or bytes already transmitted.
  A future socket writer must NOT hold this gate while awaiting I/O.

Trust boundaries: production composition must inject only the real cache-only store,
not an implementation of the interface that performs I/O. IsTls is **trusted future
adapter metadata**, not a certificate check, client input or proof that TLS/pinning
exists. Tests with IsTls=true simulate this boundary and establish no transport
security. Flag checks reject explicitly synthetic fixtures; they cannot attest
truthfulness of mislabeled records. Only real cache ownership may publish in a future
production adapter; never strip a demo flag or wire the Android demo provider here.

## Limits and security architecture

In-memory defaults: at most60 total handled attempts/minute and10 authorized attempts/
phone/minute, using monotonic time. Invalid credentials consume global budget. Phone
counter state is cleared each window and bounded by total attempts; quota counters
saturate without overflow. Rate failures return429 with conservative Retry-After60.
Method length<=8, path<=64, body length exactly0, canonical authorization exactly50
characters (Bearer plus43 base64url characters). Invalid/unknown credentials401;
non-TLS adapter metadata403; wrong method405; unsupported path/query404; unavailable
or stopped service/cache503. Authentication precedes cache access/disclosure.

These are response-core limits, not connection, header-read or TLS-handshake limits.
No socket/timeout enforcement is claimed. Proposed future transport budgets for
review:16 simultaneous connections,5-second TLS/header deadline, no request bodies,
10-second idle timeout,64KiB response maximum, no HTTP redirects/0-RTT/compression.
Transport must enforce limits BEFORE invoking this core and bind only the approved
private interface. This is currently impossible to validate as a Windows deployment
because the required certificate/profile/pairing adapters have not been implemented.

Threat model:
- Rogue LAN client: authenticate before any snapshot, per-phone revocation and quotas.
- Stolen bearer token: replay is possible until revoked. This core does NOT implement
  anti-replay TLS or sender-constrained tokens; never expose it without authenticated,
  pinned TLS and an approved enrollment lifecycle. mTLS remains an alternative.
- Guessing/overload: future256-bit issuance, constant-time verifiers, bounded parsing,
  bounded registries/quotas; connection/slow-client protection still requires transport.
- MITM/PC impersonation/DHCP change: requires pinned TLS identity/hostname validation;
  none is supplied by a boolean. Never use trust-all callbacks or cleartext fallback.
- Privileged local compromise/malicious trusted producer: outside this library's
  guarantees. Do not claim hardware-source attestation or user-approval UI from tests.
- Data leakage: fixed schema/types/errors only, no free-form logs or diagnostics.

## Pairing, opt-in and certificate plan — owner decisions before a listener

1. Separate LAN opt-in setting, default OFF on unset/new sessions; no automatic
   binding, startup service or firewall rule. Owner chooses one private interface;
   reject wildcard/public binding and stop exposure if network profile changes.
   Any necessary app-scoped Private-profile firewall rule requires explicit approval.
2. Choose mTLS client certificates versus the reviewed high-entropy bearer-token
   option. This stage's registry exercises the latter as a non-production boundary;
   it is not a decision to deploy bearer auth. No hard-coded production credentials.
3. Windows creates a per-installation nonexportable CNG/Certificate Store server key
   with appropriate user ACLs. Pin/CA material is shown physically in an expiring QR
   enrollment window; phone validates it before sending proof/token. Require explicit
   Windows-side phone approval, one-use bootstrap secrets, attempt/expiry limits and
   per-phone revocation. Android Keystore/CNG/DPAPI integration needs independent tests.
   Do not store secrets in the current monitoring/theme JSON files.
4. Approve SAN/hostname and manual-address mapping, expiry/rotation and re-pairing.
   Pin change, key loss or reinstall must fail closed; no automatic trust replacement.
   Define a user-approved revocation/rotation flow before serving data.
5. Verify native Windows certificate access, network-profile/interface change handling,
   consent UI and shutdown on Windows, plus Android pinning/Keystore later. Linux
   fake tests cannot establish these. No insecure temporary listener will be supplied.

These prerequisites are explicit blockers for a real LAN service in this stage.
Owner review is required before implementing/exposing them. No Android A2.2D work,
real-hardware exposure, merge or release is authorized by completing these abstractions.

## Verification and exact changed-file inventory

Executed with official .NET10.0.403 and existing dependencies:

```text
dotnet restore PeripheralBatteryCompanion.sln --locked-mode
dotnet build PeripheralBatteryCompanion.sln -c Release --no-restore
dotnet test tests/PeripheralBatteryCompanion.Tests/PeripheralBatteryCompanion.Tests.csproj -c Release --no-build --no-restore
```

Release solution cross-build PASS:0 warnings/errors, including Windows test assembly
compilation. Full portable suite:562 passed,0 failed,0 skipped, including28 new
service cases. Initial focused run:52 passed before two final error/shutdown cases
were added. Final full run covers those cases. Owner's native Windows test.ps1,
Windows-native test execution, tray, clipboard, TLS, pairing, network interfaces,
firewall, real gateway/HID/helper and physical-device checks were NOT RUN.

Ten generated test fixtures (nine existing projection scenarios plus one simulated
cache-only authorized response) passed unchanged draft2020-12 schema/UUID/date-time
validation. These are synthetic test scenarios; IsTls/auth were simulated, no server
was contacted. The schema and all pre-existing production files remain byte-identical.
No new dependency/project/permission, Android change or payload schema revision.

Five new files only:
- src/PeripheralBatteryCompanion.Core/SnapshotService/PublishedSnapshotStore.cs
- src/PeripheralBatteryCompanion.Core/SnapshotService/ApprovedPhoneRegistry.cs
- src/PeripheralBatteryCompanion.Core/SnapshotService/SnapshotRequestCore.cs
- tests/PeripheralBatteryCompanion.Tests/SnapshotServiceTests.cs
- docs/STAGE_A2_2C_DECISIONS.md

## Owner checklist

- Review stacked PR dependency on A2.2B; merge neither automatically.
- Run `.\test.ps1 -Release -CopySummary` on Windows and share actual results. Tests
  do not require launching the production tray or a hardware query.
- Review default-off/terminal shutdown, immutable revision/JSON consistency, revoke/
  rotation, safe errors, quotas and synthetic-flag rejection tests.
- Confirm no existing Windows monitoring/UI/consent/native code or Android files changed.
- Review the transport/auth/pairing/certificate/interface decisions above before a
  separate listener stage; do not attempt LAN connection tests against this library.
- Preserve current monitoring safety policy. Hardware and screenshot acceptance remain
  separate; no new APK is expected from this Windows-only, non-listening stage.
