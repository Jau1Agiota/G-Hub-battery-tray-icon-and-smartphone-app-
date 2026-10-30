# D3.2 inert foundations

Canonical design: [D3.1 full review](D3_1_Secure_Connection_Design_Review.txt).
Its descriptions of prior execution/publication are historical; the D3.2 owner
brief now authorizes code-only branches, pushes and draft PRs. No native approval
follows from that authorization.

New ConnectionFoundations types are unregistered, metadata-only models. No existing
production code calls them. Existing A2 prerequisites, harnesses and monitoring are
unchanged. They do not supersede native evidence checks or satisfy them with bools.
ClockSample and all trust/verifier/consent booleans are injected synthetic inputs,
not facts established by these classes. Only a future reviewed trusted local adapter
could supply real evidence; never bind these inputs directly to network JSON.

- ConsentGate begins OFF, restart clears approval, failure latches until a new local
decision; Shutdown is terminal. It cannot open or close a socket.
- AdmissionBudget serializes acquisition/release, bounds caller-supplied sizes,
concurrency and elapsed ticks. Limits are mandatory, no approved defaults exist.
It owns no timers; future adapters must enforce actual byte/time/queue bounds.
- TrustMetadataPolicy checks exact literal endpoint/IP SAN, private IPv4 or canonical
ULA, port, anchor, SPKI, validity and path/server-EKU metadata together. DNS is denied
under the inherited IP-only foundation. No certificate parsing/import/TLS callback,
constant-time native pin verification or real trust enforcement is implemented.
- PairingLifecycle serializes one synthetic claim/owner confirmation/store callback.
No secret, QR, nonce, credential or cryptographic protocol is issued. Unknown clock,
invalid claim or confirmation closes the instance; crash/lost response/storage
uncertainty cannot retry. Callback-time uncertainty cannot become success.
This conservative fake lifecycle does not select the future attempt/rate-limit UX.
- CredentialPolicy evaluates synthetic verifier match, exact snapshot.read scope,
UTC/tick bounds and in-memory revocation. Uncertain/backward clock latches deny.
Revocation denies locally before fake persistence; failure still denies. This does
not prove durable revocation across restart or authorize response writes.
- PublicationTracker rejects demo/unauthenticated/cached, negative/receding revisions
and retired sessions; duplicates never create freshness. New/advanced publications
remain UNKNOWN freshness. Disconnect yields OFFLINE but retains high-water marks.
Retired-session capacity is required; exhaustion fails closed. Instances must remain
owned across reconnect; process/credential epoch and persistence policy are unresolved.
No freshness threshold or cross-device clock trust is chosen.
- Storage/transport interfaces have test-only fake implementations; no OS/network
adapter or production registration exists.

## Frozen wire contract
Unmodified schema SHA256:
2d9ac5bf5e5263340bcd15736df7a9f6f6c201f5f4ad30b1fb7e858ec694bdc2

docs/contracts/SHA256SUMS covers original schema/example, four accepted synthetic
golden vectors, thirteen rejection cases and expected decisions. Both repositories
carry identical bytes; each test checks the anchored schema digest plus every file
digest and expected decision. Rejection cases include syntax/duplicate keys,
unknown/missing fields/enums, invalid values, demo times and dishonest provenance.
Some are stricter existing serializer invariants rather than schema-only errors.

SnapshotWirePolicy is an unwired strict bounded reader, not a general JSON Schema
engine or authenticated deserializer. It preserves all JSON fields (including nulls)
and applies the existing v1 serializer's conservative canonical identity/order/UTC/
provenance profile; no schema revision. Dynamic tests exercise synthetic live-shaped
receipt metadata without contacting a device. No parsed payload enters production.
Shared schema/golden fixtures, not Windows runtime source, are copied to Android.

## Android adapter review — deferred
Windows current/lastKnown, structured source.kind/socSupport/ownership, readAt versus
observedAt, deviceConnection, freshness and unavailableReason cannot be represented
losslessly by existing Android DeviceReading. Android Availability also lacks several
wire distinctions; connection state is transport evidence, not a wire percentage.
Do not map them into source strings, conflate receiver/headset connectivity, promote
confidence, refresh age on duplicates, invent timestamps or replace a failed live
connection with demo data. The new reader retains them; existing demo/UI untouched.
A reviewed DTO/domain/presentation extension and explicit stale thresholds are
required in D3.4. No contract or display mapping decision was invented here.

## Remaining gates
NO-GO for native proof execution now. Owner must first approve exact .NET ephemeral
certificate creation/import flags and lifetime with no named persistent side effects;
DPAPI owner ACL/nonreparse/durable envelope semantics; Android exact-anchor/SAN/pin
platform enforcement and Keystore envelope; durable revocation/response-commit
coordination; cross-restart clock/credential epochs; QR UX/session-code format;
exact interface/profile/port, certificate/token/pairing windows and resource budgets.
90-day/30-day/120-second examples remain proposals, not defaults/approvals. Numbers
in tests are synthetic boundary data only. No actual token registry was connected.
Future native proof requires an isolated reviewed plan, separate approval and honest
native/platform negative tests, then separately approved physical LAN validation.
No Option2 experiment or retired/preview ID use, cleanup, enumeration or adoption.

## Verification commands
dotnet restore PeripheralBatteryCompanion.sln --locked-mode
dotnet build PeripheralBatteryCompanion.sln -c Release --no-restore
dotnet test tests/PeripheralBatteryCompanion.Tests/PeripheralBatteryCompanion.Tests.csproj -c Release --no-build --no-restore

Android: ./gradlew --no-daemon assembleDebug testDebugUnitTest lintDebug assembleDebugAndroidTest
Do not substitute portable metadata tests for actual TLS/ACL/DPAPI/Keystore evidence.
See D3_2_Implementation_Report.txt for exact implementation commit, files and results.
