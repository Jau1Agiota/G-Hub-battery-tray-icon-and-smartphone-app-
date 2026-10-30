# D3.3A inert TLS/transport review

D3.1 and D3.2 remain authoritative. This stage adds contracts, synthetic models
and fake tests only. It does not create a working TLS server or client.

## Capability boundary
AuthenticatedTlsPeer has a private constructor and no issuer/factory. It is not
a record/DTO and cannot be created from synthetic booleans, JSON, hashes or an
IIdentityMaterialSource implementation. DisabledTlsBoundary always returns
NativeProofRequired / Denied; RetrieveSnapshot never calls its source. Even an
unexpected peer instance does not enable it. This is deliberate absence of
production authority, not a claim of security against reflection/host compromise.

Interfaces separately describe identity material observation, authenticated TLS
evidence validation, endpoint metadata policy, transport lifecycle, authentication
decision and cached read-only snapshot retrieval. No concrete I/O adapter,
registration, background service, default listener or app-startup hook exists.
InertTransportLifecycle has OFF -> CONFIGURED -> ELIGIBLE states; eligible is only
a synthetic planning decision. STOP latches until restart/configuration; restart
is OFF; SHUTDOWN is terminal. Policy sizes/timeouts/cancellation are mandatory
data, no approved defaults. Header+body arithmetic uses wide integers before
comparison. Cancellation data does not implement cancellation or guarantee exit.

SyntheticTlsSession explicitly retains synthetic labeling; exact expected host/
port, canonical private IP, IP SAN kind/value, ordered chain digest metadata/
anchor, leaf SPKI digest, path/EKU/validity and epoch-clock checks must agree.
Digest strings must be lowercase64-hex. No CN/wildcard/DNS alias fallback. Revoked
trust, bad evidence, unknown/backward clock or changed boot epoch permanently
blocks this synthetic instance. This cannot mint an AuthenticatedTlsPeer.

A future issuer must be private to a reviewed platform adapter and bind peer proof
to the actual connection, immutable verified chain/leaf/SAN/SPKI/endpoint, trusted
bootstrap record, credential/trust generation, validity and current boot/session.
It must invalidate on endpoint/trust/revocation/time/connection changes. No public
"validated=true" or hash-only factory is acceptable. Caller metadata is never native
evidence; public interfaces alone do not establish adapter trust.

## Intended platform stacks — NOT executed
Windows candidate: isolated ASP.NET Core Kestrel HTTPS endpoint using supported
.NET TLS/SslStream platform behavior with a separately reviewed ephemeral-loaded
server identity. No ASP.NET dependency added here. D3.3B must prove actual identity
key lifetime, ephemeral creation/import without named persistent side effects,
server-auth usage/chain presentation, supported TLS1.2+ with0-RTT disabled, exact
interface binding and cancellation/close behavior. Concrete API/flags still need
owner approval. Existing nonexportable-key prerequisite is NOT bypassed or satisfied
by this design; any change to that policy requires separate explicit approval.

Android candidate: dedicated per-PC HttpsURLConnection/SSLSocketFactory using
platform trust validation with a narrowly scoped exact certificate anchor and
unmodified hostname verification, PLUS exact IP SAN and SHA256 SPKI pin validation.
No instance is constructed or invoked here. D3.3B must establish that platform
chain validation, EKU/validity, IP SAN/hostname and pin checks all run before sending
credentials. No custom accept-all manager, pin-only trust bypass, system trust-store
modification, redirect, HTTP fallback or silent pin rotation. If this integration
cannot preserve validation, stop rather than introduce an insecure workaround.
Selection of a third-party TLS library/dependency would require separate review.
No claim that a self-signed leaf anchor works correctly until actual platform proof.

## Credential crash model and hard blocker
SyntheticCredentialLedger stores identifiers/metadata only, never bearer secrets.
Its atomic store contract is compare-version + complete durable flush. Receipt
must identify exactly expectedVersion+1. Commit conflict, malformed/rollback/future
ack, partial record, exception or uncertain outcome leaves all authorization denied.
Issuance: no entry until acknowledged. Rotation: atomic replacement invalidates old
label; labels cannot be reused. Revocation denies locally before persistence and
blocks reentrant reads during commit. Repeated confirmed revoke is idempotent.
Read/revoke serialize under a lock. This is not authorization of a later socket
write: a real adapter needs revocation-generation coordination at response commit.

Crash/lost acknowledgment closes the current instance. Callback-time crash cannot
be overwritten by a success acknowledgment. All restored instances are
RESTART_LOCKED with NO unlock/recovery API, regardless of stale/partial/corrupt/
rolled-back store contents. "newEmptySyntheticSession" is a fake-test setup input,
not local consent, provisioning, a production reset or a recovery mechanism.
No storage read/import path exists. Capacity and time bounds are injected; generation/
version increments check overflow; publication revision compares without incrementing.
EpochClock rejects clock uncertainty, regression and boot change, never compares
monotonic ticks across boots. Boot IDs/time are injected test data, not OS proof.

Hard blocker: no robust, authenticated durable anti-rollback/revocation recovery
has been established. DPAPI/Keystore confidentiality alone cannot prove an old
protected image is current. Native persistence/rollback anchors, write ordering,
response-generation semantics and lost-ack recovery need separate design and
approval. Until resolved, restart must remain locked; no production credential
store, issuance or restore path is implemented. Model success is not durability proof.

## Parity and wire hardening
Existing v1 schema/example and D3.2 fixtures/digests unchanged.
Added identical docs/contracts/d3-3a fixtures:46 identity traces,12 credential
traces,9 freshness steps and9 malformed byte payloads, with digest manifest.
Both suites run identical expected decisions in C# and independently written Kotlin.
IPv4 canonical/private ranges, ULA canonical compression, unsupported forms,
integer overflow, clock/boot rollback, revoked trust and retired sessions are covered.
Unknown freshness remains unknown, demo cannot become live; no lossy Android adapter.

Windows reader now explicitly rejects malformed UTF8 and noncanonical numeric
forms rather than relying on platform parser behavior. Kotlin guard also rejects
negative-zero integer and unpaired Unicode surrogate escapes. Shared cases cover
duplicate keys (existing D3.2), unknown fields/enums, malformed UTF8/escapes,
fraction/exponent/overflow/leading-zero revisions and trailing data. Payload limits
remain unchanged. These readers are not a complete schema engine or network parser.

## Validation incident and prevention
The initial unfiltered portable-project run executed legacy
PublicServerCertificateInspectorTests: ECDsa.Create, CertificateRequest,
CreateSelfSigned and public certificate loading. Two older real-secret generation
tests also ran. This exceeded this stage's no-native-crypto/certificate boundary.
The run is retained and disclosed; it is NOT D3.3B TLS proof. The new D3.3A tests
themselves use only synthetic metadata and in-memory fakes. No Option2/native
fixture, real TLS handshake, listener, OS certificate-store mutation, DPAPI,
Keystore, phone installation or physical LAN test was invoked.

Subsequent local run explicitly excludes those11+2 legacy cases. Original
assertions/classes and ordinary test.ps1 remain unchanged. New
scripts/Test-D33ACodeOnly.ps1 supplies the documented filter. On this exact D3.3A
branch (push or PR head), CI runs that scoped Linux job; the ordinary Windows full
test/publish-artifact job is skipped. Other branches keep their original workflow.
PowerShell is unavailable in this cloud executor; script execution is left to CI,
not falsely claimed locally. Source/command review and equivalent local dotnet
commands were executed. Excluded tests are not reported as runner "skipped".

## D3.3B separate approval questions / rollback
Before any native proof approve:
- exact supported OS/runtime/device versions and isolated no-production scope;
- ephemeral .NET certificate/key lifecycle/API flags, no named persistence and
  ownership-safe disposal, with no reuse of old experiment IDs/resources;
- review of existing private-key protection requirements before any policy change;
- Android exact-anchor/chain/EKU/validity/SAN/pin enforcement and connection binding;
- test-only identity lifecycle and synthetic credential data, no real user secrets;
- explicit decision whether a loopback/listening or two-device handshake is allowed,
  exact bind/interface/port/firewall scope and cleanup ownership;
- DPAPI/ACL and Keystore work separately if required, durable revocation/anti-rollback
  design, boot/time authority and response-commit race handling;
- negative cases, failure stop conditions, resource budgets and no automatic retries.

No essential D3.3A test needs native TLS; native proof is deferred, not bypassed.
GO only for review of code-only changes; NO-GO for D3.3B execution until approval.
Rollback: leave both branches unmerged or revert only the D3.3A commits after owner
review. No real transport/identity/credential state was created by the new code;
no old artifact or key cleanup is authorized. Preserve original source/untracked work.
See D3_3A_IMPLEMENTATION_REPORT.txt for commands, counts, commits and limitations.
