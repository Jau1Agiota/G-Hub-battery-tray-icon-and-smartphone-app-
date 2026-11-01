# Stage E3.1 — Security contract for review

Date: 2026-10-31. Status: PROPOSED CONTRACT; requires owner approval before E3.2.
MUST/MUST NOT describe future implementation requirements, not implemented features
or authority to execute them. Gate E remains BLOCKED. Gate B remains independently
scoped. No implementation, native operation, listener, credential/certificate
generation, live connection, commit, push, merge or release is authorized here.

## 1. Baseline and evidence

Inspected clean local feature/stage-e2-snapshot-interop branches:
- Android dc19ab7fa165fc47d51725547d46164603659080.
- Windows ebca523c47aab663bff944d85b82881c131e2168.

Owner's E2 closure briefing reports Windows-native locked restore, 84 producer
assertions and 29 exact-byte comparisons passed. This is accepted owner-reported
serialization evidence, not native security or live-connectivity evidence.
Previous Android evidence: 223 tests passed; debug/release builds passed; lint
0 errors/9 existing warnings. These checks were not rerun for this document.

Implemented: production projection, immutable publication/serialization, inert
request core GET /v1/battery-snapshot, in-memory bearer verifier registry, strict
Android parser and offline/mock session/replay paths. Missing: approved native
identity/storage adapters, real TLS evidence issuer, durable authorization/replay
state, production enrollment and network transport. IsTls input is not TLS proof.

Sources: Windows SnapshotService/{PublishedSnapshotStore,SnapshotRequestCore,
ApprovedPhoneRegistry}.cs; TransportSecurity/{SecurityContracts,LanApprovalSession,
PublicServerCertificateInspector}.cs; ConnectionFoundations/{InertPolicies,
TlsTransportBoundary,SnapshotWirePolicy}.cs; docs/W1_PREP_APPROVAL_GATES.md,
STAGE_A2_2C4_NATIVE_PROOF_GATE.md, STAGE_A2_2C5_ISOLATED_SECURITY_FIXTURE.md.
Android docs/stage-a2-3c-security-architecture.md, stage-a2-3d-mock-session-policy.md,
stage-e2-snapshot-interop.md; contract/, mocksession/, domain/, ui/ and manifest.

## 2. Scope and historical conflicts

Initial deployment: exactly one PC and one enrolled phone. Android initiates
foreground-only HTTPS requests every 10 seconds, completion-relative, one in flight.
No discovery, cloud, automatic router/firewall configuration, background service,
remote device control or provider refresh. Windows serves only already-published
immutable bytes. Existing local monitoring remains independent.

| Historical conflict | E3.1 proposed resolution |
| --- | --- |
| Bearer versus mTLS | Pinned HTTPS plus independent random 256-bit phone bearer; no mTLS in initial deployment. |
| DNS authority versus IP SAN | Exact canonical RFC1918 IPv4 address and iPAddress SAN; no DNS, CN/wildcard fallback or hostname remapping. IPv6 deferred. |
| Public trust versus self-signed identity | Explicitly enrolled self-signed server leaf is an application-scoped trust anchor, plus SHA-256 DER-SPKI pin and full reviewed leaf checks. Never modify OS trust. |
| /v1/snapshot versus request core | GET /v1/battery-snapshot; unchanged BatterySnapshotV1 body. |
| Persisted versus ephemeral server keys | Proposed persisted nonexportable CurrentUser software-CNG P-256 identity, contingent on independent native custody proof. Ephemeral alternatives do not satisfy this contract by substitution. |
| Equal revision after reconnect | Authenticated challenge-bound duplicate may establish channel responsiveness, but cannot advance publication or measurement freshness. Existing mock/offline behavior stays unchanged. |
| Registry supports 16 phones | Deployment policy admits one active phone; a replacement requires explicit revocation/re-enrollment. Do not weaken existing registry bounds. |
| Parser lexical differences | Keep E2 documented uppercase-UUID/-0 differences for now; emitted canonical lowercase/positive revisions are interoperable. Any lexical tightening is separate approval. |

The IP-SAN choice does not make existing DNS-oriented code compatible. Future
reviewed changes must replace the endpoint identity representation coherently in
approval, public-certificate inspection and TLS validation, preserving fail-closed
checks. Until then there is no eligible production adapter. Historical documents
remain evidence; this proposed contract grants no permission to bypass them.

## 3. Threat model

Assets: PC identity/key, phone bearer, enrollment secret, authorization generation,
revocation records, replay history, snapshot confidentiality/integrity, user consent,
bounded resource use and truthful UI evidence.

Trust boundaries: local Windows approval UI; Windows protected identity/state;
untrusted LAN; Android TLS/credential storage; parser/session/evidence/UI boundary.
Monitoring consent is not LAN consent. Payload identifiers are not authentication.

Attacker capabilities: observe/drop/delay/reorder traffic, spoof IP/DHCP/ARP, operate
a rogue server/client, copy exposed pairing material, steal a phone/bearer, replay
files/backups, send hostile HTTP/JSON and exhaust resources. A stolen bearer is
reusable until expiration/revocation; TLS does not sender-bind it.

Assumptions: patched supported OS, trusted local owner interaction, uncompromised
approved app binaries, cryptographic RNG, and trustworthy-enough UTC for validity
decisions. Same-user malicious code, administrator/root compromise, physical battery
accuracy and traffic-analysis resistance are outside the protection claim. A trusted
PC may report wrong readings. Hostile networks can always deny availability.

Required defenses: out-of-band exact trust enrollment, TLS and pin validation before
secrets, explicit owner approval, distinct expiring phone authorization, protected
state, challenge correlation/replay checks, strict bounded parsing, no fallback,
sanitized diagnostics and local revocation. QR theft/racing and storage rollback
remain explicit residual risks; neither hashing nor encryption alone removes them.

## 4. Transport and certificate profile

HTTPS on an owner-selected RFC1918 IPv4 interface/address. Proposed configurable
port 8443; no port is allocated now. Bind only that address; reject unknown/public
network profile. New/unset LAN exposure is OFF. Require fresh Windows local approval
per process/session, proposed maximum 8 hours and never past certificate expiry.
Restart, suspend/resume, NIC/address/profile change or uncertain evidence invalidates
approval; restoring evidence does not automatically restart exposure.

TLS 1.3 only for initial deployment. TLS 1.2 requires a reviewed compatibility
amendment. Disable 0-RTT, redirects, cookies, proxy routing and HTTP caching. No
cleartext fallback, origin changes or user override of certificate errors. Proposed
HTTP/1.1-only transport limits complexity; no HTTP upgrades or HTTP/2 negotiation.

Proposed leaf: ECDSA P-256, non-CA, DigitalSignature, serverAuth EKU, exactly one
approved iPAddress SAN, no DNS SAN, maximum 90-day validity. Validate signatures,
algorithm/critical-extension constraints, exact enrolled public DER anchor, validity,
SAN, usage and SHA256(DER SubjectPublicKeyInfo); pin success cannot waive checks.
Self-signed enrollment is explicit scoped trust, not ordinary public-Web PKI or a
trust-all callback. No AIA/OCSP/CRL fetching is required for this enrolled leaf;
local distrust/re-enrollment is its revocation mechanism. Validation feasibility
on actual Android TLS stack is an unresolved platform proof, not assumed success.

Manual identity rotation or IP change requires fresh enrollment. No overlapping
pins, automatic renewal or remotely authorized identity replacement initially.
Warn before expiry (14 days proposed); expiration stops operation without grace.

## 5. Wire contract: snapshot channel

Security protocol version 1 is independent of snapshot schemaVersion 1. Exact
required headers, case-insensitive names, exactly one value each:

Request: GET /v1/battery-snapshot, no query/body/transfer encoding;
Authorization: Bearer <43-character unpadded canonical base64url 32-byte token>;
PBC-Protocol: 1; PBC-Request-Nonce: <same encoding of fresh 32 random bytes>.

Successful response: 200, Content-Type application/json (optional charset=utf-8),
Cache-Control: no-store, Content-Length exact, no compression/transfer encoding;
PBC-Protocol: 1; PBC-Request-Nonce: exact request value;
PBC-Publisher-Instance: canonical lowercase UUID;
PBC-Authorization-Generation: canonical positive decimal, at most Int64.MaxValue;
PBC-Snapshot-SHA256: lowercase 64 hexadecimal characters.

Body MUST be the exact immutable PublishedSnapshot.Json bytes, never reserialized
by transport. Publisher header MUST equal body serverInstanceId and come from the
same atomic publication read. Digest covers these exact bytes. No 304 responses.
Headers/digest are authenticated by TLS; they are not standalone signatures or
proof against a compromised PC. No additional custom cryptographic protocol.

Client validates authenticated origin/authorization context, version, outstanding
nonce, authorization generation, framing, byte limit, digest, strict parser,
publisher binding and replay policy before atomically publishing state. Reject
duplicate security headers, malformed encodings, synthetic bodies and incompatible
versions. Reject unexpected paths, methods, queries, redirects and ambiguous framing.
Generate a new nonce per attempt; never reuse/retry an old request automatically.

Errors retain generic existing status categories: 400 invalid request, 401 invalid
authorization (including expired/revoked), 403 secure transport policy, 404/405,
429 bounded Retry-After, 503 disabled/no publication. Error bodies <=1 KiB; no
credential existence details, raw exception text or sensitive telemetry. An
authenticated 503 can indicate endpoint responsiveness, but cannot establish a
successful snapshot connection. Server checks current authorization immediately
before queuing a response write; revocation cancels queued/active responses.
Bytes already delivered cannot be recalled; no registry lock across network I/O.

Limits proposed: 4 admitted connections, no waiting connection queue, 8 KiB headers,
5 s handshake, 5 s headers, 5 s snapshot exchange, 10 s idle, 5 s shutdown,
65,536-byte snapshot body/depth 16, no decompression. Client checks bound while
streaming, not after unlimited allocation. Preserve existing 60 global/10 phone
requests per minute; admission also covers pre-authentication/TLS costs. Enrollment
has separate bounded budgets; snapshot MaxBodyBytes remains zero.

## 6. Manual enrollment and credential lifecycle

Exposure approval and opening enrollment are separate local owner actions. One
120-second enrollment window and one claimant; no numerical-code-only bootstrap.
QR contains strict UTF-8 JSON <=8 KiB, depth <=4, exact keys:
protocol=1, endpoint=https://<approved-ip>:<port>, leafDer (unpadded base64url,
decoded <=4 KiB), spkiSha256 (64 lowercase hex), enrollmentId (lowercase UUID),
bootstrap (32 random bytes as canonical base64url), expiresAt (canonical UTC).
Reject unknown/duplicate keys and endpoint paths/userinfo/query/fragment. QR expiry
is enforced by Windows UTC and monotonic deadline; phone cannot extend it. Never
log, back up, place in URL or persist bootstrap material.

Phone verifies QR bounds/leaf identity and local certificate validity, then validates
the actual TLS peer before sending any proof. Phone locally creates its 32-byte
bearer and protects it before claim. Windows receives only SHA256(raw bearer) during
enrollment; bearer appears later only in TLS-protected Authorization headers.

Proposed enrollment routes (new future adapter, not existing request core):
- POST /v1/enrollment/claim: bootstrap in Authorization bearer header, strict JSON
  body <=1 KiB containing enrollmentId, phoneId (random UUID), phoneNonce (32 bytes
  base64url), tokenSha256 (64 lowercase hex). One-use claim returns 202 and locks
  the window to that exact tuple. No active authorization exists yet.
- GET /v1/enrollment/status: bootstrap header plus PBC-Enrollment-Id; no body/query.
  Status is pending/approved/rejected with phoneId, authorizationGeneration and
  issuedAt/expiresAt only when approved. Max one status request per 5 seconds.
  Bootstrap grants only this claimed enrollment's status, never snapshots/another claim.

PC and phone display the full SHA256 of this exact ASCII transcript (LF after every
line): PBC-ENROLL-1, endpoint, spkiSha256, enrollmentId, phoneId, phoneNonce,
tokenSha256. Each item occupies one line; encodings above forbid embedded controls.
User MUST compare all 64 hex characters using the grouped displays and confirm on
the PC. No truncation or device-name-only confirmation. This is a confirmation
binding, not a password-based authentication scheme; QR theft still enables a race,
which must fail without matching explicit owner approval.

Owner approval atomically persists active phoneId/verifier/scope snapshot.read,
authorization generation, issue/expiry times and enrollment result before reporting
approved. Deadline applies through durable approval. Keep claimed status readable
only until the same 120-second deadline. At timeout, rejected/unclaimed/pending
records cannot activate. If approval committed but response was lost, PC shows the
possibly orphaned active phone; require local revoke before a new enrollment.
No blind claim retries, automatic reissuance or extra credential on ambiguity.

Initial credential lifetime: 90 days from durable approval, capped by enrolled leaf
expiry; no automatic renewal or grace. Warn at 14 days. Windows enforces expiry on
each request. Phone clock/record does not override server authorization. One active
phone; replacement revokes the old generation first. No credential reuse across PCs.

Revoke locally on Windows, durably commit tombstone/generation change before
acknowledging, deny new reads and cancel active work. Persistence uncertainty stops
serving. Phone Forget deletes local secret/trust/history and explains that PC-side
revocation remains necessary. Lost phone, invalidated Keystore key, identity change,
reinstall or corrupt state requires fresh explicit enrollment. No network recovery,
automatic backup restore, token export, clipboard transfer or plaintext fallback.

## 7. Protected storage and native proof requirements

Windows identity proposal: CurrentUser software CNG, P-256 signing-only,
nonexportable, user-restricted ACL, verified certificate association. Preserve
NonExportable AND CurrentUserAclRestricted guards. Enrollment/revocation state:
DPAPI CurrentUser plus restrictive file ACL, versioned identity binding, atomic
replacement/durable commits and independently tracked generations/tombstones.
Store verifier only, never raw bearer. No store/DPAPI/CNG operation authorized now.

Android: AES-GCM credential protection using a nonexportable Android Keystore key;
fresh unique IV for each write, authenticated associated data binding protocol,
endpoint, SPKI, phoneId and authorization generation. Bind certificate/trust and
replay metadata to the protected record. App-private, no backup/device transfer;
no external/shared storage. Do not claim hardware backing without evidence.
Foreground app use does not necessarily mean biometric authentication each poll;
the exact Keystore access configuration requires platform review.

Missing/corrupt/inconsistent records fail closed. Encryption, DPAPI and file
generations do NOT independently prove rollback resistance. Full hostile restoration
of an old consistent state is outside initial assurance unless a separately reviewed
rollback-resistant anchor exists. Never claim that this risk was solved. Restart
requires fresh local Windows exposure approval and state-consistency review;
suspected restore requires revoke/re-enroll. Arbitrary backup restore is unsupported.

Do not log tokens, bootstrap/QR, Authorization headers, raw bodies, IP/phone names,
private material or local paths. Diagnostic export uses bounded reason codes and
explicit consent. Sensitive data in managed memory is minimized; complete erasure
or resistance to same-user/admin malware is not claimed.

## 8. Publisher, replay and freshness

Stable PC trust identity is bound to enrolled SPKI/leaf; a local identity-generation
record separates replacement identities. publisherInstanceId is a distinct random
UUID per PublishedSnapshotStore lifetime, never restored with revision reset.
Create it only in future approved runtime implementation. Increment revisions only
on publication, checked for overflow; overflow stops publication rather than wraps.

Client scopes high-water marks by PC trust identity/generation and publisher UUID.
Within a bound instance: higher revision accepts; equal revision/exact bytes is a
duplicate; equal/different bytes or lower revision fails. Store digest/byte evidence
and revision atomically before displaying accepted data; no accept on failed write.
Reconnect retains marks. One outstanding request and fresh nonce bind the current
publisher to a newly authenticated response, not a free-standing payload UUID.
New instance retires old instance; reuse of any retired instance is rejected.
Proposed maximum 64 recorded instances per enrollment; no silent eviction. At limit,
stop and request owner-reviewed recovery/re-enrollment.

On initial enrollment/new publisher, a fully validated challenge-bound snapshot may
establish connection even if source data is stale. New publisher does not reset
measurement timestamps or establish fresh hardware observations. Process recreation
must load durable history; missing history for existing trust requires recovery.

Channel liveness: a timely validated challenge response from current trust and
authorization; identical snapshot can refresh channel time only. Proposed deadline
30 seconds since last successful snapshot response. Snapshot progression: higher
revision, separately tracked. Measurement freshness: original source evidence,
readAt/observedAt/freshness/availability/precision/ownership, never polling success.
capturedAt is publication time, not a hardware observation. UTC uncertainty or
backward/overflowing monotonic time fails the affected session/trust action.

Preserve current vs lastKnown, null vs zero, source kind/support/ownership, reason,
deviceConnection, timestamps and HyperX estimate/ambiguity in coherent UI state.
Never promote lastKnown to current or unsupported to disconnected. Unknown source
age remains unknown. Initial UI reports wire freshness and explicit age when known;
no new age-based claim of FRESH. Source-specific extra stale thresholds are deferred
until source cadence evidence is agreed, and may only downgrade confidence.

## 9. Connection lifecycle and failure policy

DISCONNECTED -> CONNECTING -> AWAITING_VALID_RESPONSE -> CONNECTED only after all
TLS/auth/version/nonce/parser/publisher/replay checks. CONNECTED means authenticated
snapshot endpoint responsive; individual peripherals may be stale/unavailable.
Publish session, evidence and display as one immutable state. Every attempt has a
local generation; cancel/replacement invalidates it and late callbacks are ignored.

Transport loss immediately leaves CONNECTED; default hides displayed values while
retaining protected evidence separately. No persistent telemetry cache initially;
only minimal protected replay metadata/digests. Optional stale-value display requires
separate approval. Backgrounding stops polls/cancels work/invalidates generation;
resume starts fresh authentication with history retained, no background scheduler.

Transient failures: retry after 10, 20, 40, then 60 seconds, with positive-only jitter
up to 20%, no concurrent attempt; cap at 5 consecutive failures then explicit Retry.
Validated Retry-After decimal seconds 1..300 delays at least that long; missing or
invalid value uses backoff. Retries cannot bypass current foreground/approval state.
Trust/pin/version/replay/parser/auth/storage/clock failures enter ERROR and stop
automatic retry; explicit recovery is required. Do not silently fall back to demo.
No definite PC-off assertion from network timeout. Owner disable/shutdown stops
admission immediately, cancels work and bounds drain; failure to stop is reported.

## 10. Acceptance tests and gate boundaries

| ID | Required evidence before production acceptance |
| --- | --- |
| T1 | Wrong pin/anchor/IP SAN, DNS/CN/wildcard substitution, bad usage/CA/critical extension, expired/not-yet-valid leaf, TLS downgrade/redirect rejected before secrets. |
| T2 | QR parser bounds/duplicates, bootstrap theft/race, expired/reused claim, wrong transcript, rejected owner confirmation, second phone and interrupted approval fail safely. |
| T3 | Missing/malformed/wrong/expired/revoked bearer; distinct generations; revocation races before queue/write; uncertain durable revocation stops serving. |
| T4 | Wrong-user/corrupt/interrupted storage, Keystore invalidation, restart, unsupported backup restore, no plaintext fallback; residual full-rollback risk documented. |
| T5 | Higher/equal/changed/lower revisions, response reorder, wrong/reused nonce, instance mismatch/reuse, reconnect/recreation/restart, capacity exhaustion, atomic state write. |
| T6 | Duplicates refresh only channel; source times/null/zero/stale/unsupported/lastKnown/HyperX ambiguity preserved; no measurement freshness from new publication. |
| T7 | Header/body limits, malformed UTF-8/JSON/duplicate keys, chunked/compressed/truncated responses, slow handshakes, admission flood and bounded cancellation/shutdown. |
| T8 | Foreground/background, process death, NIC/profile/IP change, owner disable, suspend/resume, expiry, retry budgets and late callbacks. |
| T9 | E2 84 producer assertions/29 byte comparisons, canonical hashes unchanged, existing Android tests/build/lint and focused Windows regressions preserved. |
| T10 | Separately authorized real Windows/Android enrollment and HTTPS reads; unchanged snapshot, stale readings, restart, lost Wi-Fi, revocation, expiry and recovery, with exact results/limitations. |

E3.2 may implement pure deterministic policies and tests ONLY after explicit owner
approval of this contract and E3.2 scope; no crypto/native adapters or production
wiring implicit. Existing mock/simulation semantics and E2 fixtures remain intact.

Gate B for new sensitive harnesses requires independently reviewed sources, SDK,
dependency resolution/imports/generators/test discovery, hashes and exact permitted
restore/build/discovery commands. E2 build evidence grants no such approval.
Native Gate E requires separate exact source/binary/runtime provenance, invocation,
one-shot consent, resource allowlist, time limits, stop/ownership/cleanup evidence
and review. Existing offline W1 scope excludes TLS, socket, DPAPI, Keystore and
credential operations; success cannot authorize them. These need separately approved
plans. No guard removal, permit fabrication, automatic retry or broad test selector.

Project connectivity Gate E remains BLOCKED until all required platform evidence,
negative-security tests, exact exposure scope and successful end-to-end tests have
been independently reviewed and the owner explicitly approves the gate transition.
A native experiment and production connectivity gate are distinct approval scopes.

## 11. Approval decisions and residual blockers

Approve/revise this proposed contract as a unit, especially:
1. Self-signed application-scoped leaf anchor + SPKI/IP SAN, IPv4-only/TLS1.3-only;
   resolving older DNS contracts later without relaxing custody/exposure guards.
2. Phone-generated bearer/verifier enrollment and full 64-hex transcript comparison;
   UX is deliberately demanding; any shorter comparison needs separate analysis.
3. 120-second enrollment, 90-day leaf/credential maximum, 8-hour exposure approval,
   manual rotation/re-enrollment, one phone and no stale-value display initially.
4. Challenge-bound duplicates establish liveness only; durable high-water marks,
   64-instance cap and explicit recovery on missing/uncertain state.
5. Residual bearer theft, same-user/admin compromise, QR race, full storage rollback,
   trusted-clock and traffic-denial risks accepted only within stated assumptions.

Platform proof remains a blocker: exact Android application-anchor validation,
Windows persistent CNG/TLS association and custody, DPAPI/Keystore crash/restart
behavior and authorization/revocation durability are unverified. Native test scope,
supported OS/device matrix and selected production endpoint require later explicit
review; none is invented here. E3.1 approval permits no E3.2 work unless separately
requested, and neither approval permits native execution or live connectivity.

Rollback: design rejection changes nothing. Future pure-policy work stays unwired;
future exposure defaults OFF. Disable/cancel on uncertainty, preserve revocation
records and exact-owned resource evidence; never broad-delete keys or reset trust
history to make a test pass. No automatic merge/release.

## 12. Conditional approval finalization — E3.2 boundary

Owner conditionally approved architecture and authorized pure E3.2 policies only
following these clarifications. This addendum governs any ambiguous earlier wording.

- HISTORY_FULL: the 65th distinct publisher is denied before changing the current
  publisher, history, evidence or display. Latch RECOVERY_REQUIRED; even previously
  valid traffic is denied thereafter. Never clear/evict/roll back history, reset the
  cap, adopt a new UUID or recreate an engine to resume under existing authorization.
  Recovery requires local owner review, durable revocation of the old authorization,
  and separately approved fresh enrollment/trust generation. E3.2 has no recovery API.
- REVOKE: deny in memory and invalidate all in-flight/queued response generations
  immediately, before attempting persistence. Acknowledge completion only after
  durable tombstone commit. Not-committed/uncertain persistence keeps serving OFF;
  restart must not resurrect authorization. Already transmitted bytes cannot be
  recalled. A prepared response is not authority to send: recheck at release.
- EXPIRY: deny at now >= expiresAt, including preparation/release/duplicate/reconnect.
  Also enforce a monotonic lifetime bound so backward clock adjustments cannot
  extend authorization. Uncertain/backward clocks latch failure. No grace, retry,
  cached approval or queued response can bypass expiry. Restarts require verified
  records and fresh local exposure approval, not a reconstructed monotonic deadline.
- TRANSCRIPT: bind exact endpoint including port, approved leaf/SPKI, enrollment
  version/id, phone id/nonce and token verifier. The QR leaf DER must match its SPKI;
  full leaf identity is checked against the live peer. Reject unapproved endpoint,
  certificate, redirect or transcript substitution before any proof is sent. The
  transcript format already contains SPKI; the enrolled exact leaf DER is an
  additional mandatory context binding. Matching text alone never proves TLS.
  Endpoint changes require new local approval/enrollment, never LAN-provided updates.
- PERSISTENCE: calculate candidate state without publishing it; durable commit must
  precede accepted replay/evidence publication. NOT_COMMITTED and UNCERTAIN both
  deny and latch recovery. Retain previous evidence as non-display audit state only.
  No retry/reload/restore implicitly clears the latch. No accepted UI state is rolled
  back to an older revision. Missing/inconsistent state for existing trust also
  requires recovery. Detection of restored older state fails closed; undetectable
  full consistent storage rollback remains an explicit unproven protection limit.
- GATES: no Gate B/E guard, permit, default, native proof or authorization is changed.
  Gate E remains BLOCKED; Gate B remains independently scoped. Existing offline
  Android JVM tests may exercise test-only policy models. No sensitive Windows test
  discovery, build or native harness is authorized by this addendum. E3.2 cannot
  issue trust, credentials, enrollment, native storage or TLS evidence. All model
  identity/time/commit/peer observations are explicit synthetic test inputs.

E3.2 models are behavioral specifications only. Their acceptance of injected
observations is not an authentication boundary or a durable-security proof.
