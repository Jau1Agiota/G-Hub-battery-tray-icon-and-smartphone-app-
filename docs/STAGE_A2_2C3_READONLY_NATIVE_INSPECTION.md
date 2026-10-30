# A2.2C3 — read-only native inspection slice; still non-listening

## Decision record / exact base

Implement the brief's permitted smaller inspection components. Do NOT claim complete
native security foundations, a trusted certificate, protected enrollment store or
working TLS. Provisioning requires Windows proof of nonexportability/key association/
ACLs; DPAPI storage requires executable Windows corruption/restart/revoke/rotation
tests. This Codex environment is Linux, so those conditions cannot be satisfied here.
No insecure temporary implementation substitutes for them.

Base: `41ab8c15d3175cdbfb700c5ec8ed653b192846c9`, A2.2C2/PR10, open when inspected.
PR8/9/10 states and changes, AGENTS.md, A2.2B/C/C2 reports/schema and current cache/
lifecycle code were reviewed. Branch: feature/stage-a2-2c3-readonly-native-inspection;
draft PR stacks on C2. No merge/rebase. Owner baseline: .NET10.0.402 Release restore/
build PASS,639 tests passed/0 failed/skipped. Tray/physical checks NOT RUN. That is
owner evidence, not a native run in Codex. Android remains read-only at A2.1.

Confirmed owner decisions: direct home local-Wi-Fi FIRST; Tailscale later, optional.
Pinned TLS with SPKI+hostname/certificate validation and a unique256-bit bearer per
approved phone; no mTLS implementation. Explicit opt-in/revoke/rotate, no forwarding
or automatic firewall changes. Historical generic mTLS policy options are unchanged
but unactivated; no new mTLS code was introduced.

## Actually implemented

1. PublicServerCertificateInspector (Core) examines supplied public certificate data
   offline: DER-SPKI SHA256, exact SAN matching (no wildcard/CN fallback), validity,
   unique required extensions, end-entity BasicConstraints, explicit server-auth EKU
   and digital-signature usage. Caller supplies expected authority/pin/time; no final
   SAN, lifetime, port or certificate is silently selected.
2. ReadOnlyWindowsSecurityInspection (Windows, not registered in startup): exact
   interface ID/canonical assigned-address lookup via NetworkInterface APIs, actual
   Up/type metadata, with profile UNKNOWN. No private-prefix/Wi-Fi membership claim
   is converted to Private authorization. Unknown profile cannot pass the C2 gate.
3. Exact CurrentUser/My certificate lookup by caller-supplied SHA1 thumbprint used
   ONLY as a store index. Security pin is SHA256(SPKI), not that thumbprint. Opens
   ReadOnly|OpenExistingOnly, with validOnly=false to avoid implicit chain/revocation
   fetching, then performs the offline public checks. Disposes certificate wrappers.
   Missing/ambiguous/inaccessible selection fails closed; no automatic replacement.
   HasPrivateKey is reported association METADATA only; no private key is opened,
   signed with, exported or provisioned. Key protection is explicitly NotInspected.
4. Eleven new portable certificate cases plus four Windows-only read-only cases.
   Public tests create/dispose synthetic in-memory ECDSA certificates/keys (including
   CA-shaped negative fixtures), reload public DER only, and install NOTHING in OS
   stores. Native tests contain no certificate/key/store/profile/firewall writes.

Important: PublicFieldsMatch does NOT validate signature chain/trust, revocation,
private-key possession, CNG nonexportability or ACLs. It is deliberately NOT a TLS
RemoteCertificateValidationCallback and must never be used as one. A self-signed
untrusted PUBLIC test certificate can match these fields; that does not authorize
TLS. The native helpers do NOT implement the full IWindowsSecurityEvidenceSource;
profile/key/storage evidence is incomplete, so they cannot enable the LAN gate.
No listener, event subscription, certificate provisioner or credential writer exists.

## Preserved behavior / actual audit

Existing TrayApplicationContext dispatches cached Logitech/HyperX changes and joins
query/schedule/monitor work during shutdown. C2 opt-in is still unregistered/default
OFF; persisted desired ON cannot restore session approval. No production startup,
settings, UI, shutdown, monitoring cadence/consent/safety, SQLite, G HUB gateway,
HID/helper, request handler or publisher file changed. Snapshot schema v1 is byte-
identical, including stable IDs, null/zero, lastKnown, uncertainty and separate times.
No Android file, dependency, permission or project configuration changed.

## Owner decisions REQUIRED before provisioning (proposals, NOT defaults)

Please explicitly approve or revise these before native provisioning/storage work:
- Authority/SAN: anonymous per-installation DNS identity such as
  pbc-<random-installation-id>.local, not machine name or hardware ID. Match exact
  SAN and pin; a dedicated Android resolver may map that authority to manual LAN IP.
- Port/interface: suggested unprivileged8443 IF available; owner selects the actual
  private home LAN NIC/address, with no automatic fallback. Confirm PC Wi-Fi versus
  wired-home-LAN attachment; phone's first physical test is local Wi-Fi, not Tailscale.
- Certificate: proposed90-day validity, software CurrentUser CNG ECDSA P-256 key,
  signing use, no export/PFX, verified user-restricted key ACL and certificate/key
  association. No system root or machine-key install. Exact native access/ACL policy
  must be reviewed and tested on Windows, not inferred from HasPrivateKey.
- Rotation: initial conservative manual owner-driven rotation, stop/re-pair for new
  SPKI/key or reinstall/key loss; no silent replacement or trust migration. Decide
  whether same-key renewal is permitted and how it is selected/approved.
- Consent/UI: separate LAN controls, default OFF; explicit session approval of
  identity/interface/auth choice, disable stops transport independently of monitoring.
  Persist desired choice only; no restored active approval. No UI was added here.

These proposals do not change code defaults or deploy an identity. Provisioning stays
blocked pending explicit owner approval and Windows-native proof. No port reservation,
certificate creation or provisioning test side effect occurred in Codex.

## Pairing/bootstrap design for later Android work — not implemented

Owner opens a local-only QR window (proposed2minutes/5 attempts). QR carries protocol,
authority/address/port, server SPKI pin and one-time bootstrap proof; no secret goes
in an HTTP URL/log. Phone verifies explicit trust/pin, exact hostname, certificate
validity and full TLS validation BEFORE requesting credential delivery. Windows
shows the requesting phone and requires explicit local approval. Atomically consume
bootstrap once; deny reuse, expiry, guessing and concurrency races. Generate a unique
CSPRNG32-byte bearer only on that trusted owner-approved path. Windows stores only
its SHA256 verifier; Android stores the bearer encrypted with a Keystore-backed key.
This stage performs no enrollment, credential delivery, phone connection or pairing.

Revocation/rotation must affect all subsequent requests and applicable active
sessions; already disclosed data cannot be recalled. Lost phone, PC key loss,
reinstall or unprotectable state requires explicit recovery/re-pairing, not default
credentials or automatic trust fallback. No raw bearer tokens in Windows persistence.

## DPAPI model and unresolved proof obligations

No DPAPI storage code is shipped: the brief requires Windows-native execution first.
Proposed separate versioned/bounded protected registry: anonymous phone IDs, verifier
hashes, revocation/rotation revision and server-identity binding. Protect with DPAPI
CurrentUser plus reviewed ACLs; never monitoring/theme JSON. Validate integrity,
structure, version and identity before loading. Write a same-directory unique temp,
flush durable data, atomically replace, and clean only owned temp files. Corruption,
access failure, missing expected file or interrupted update must fail closed and
must NOT silently load an older backup/revive a revoked credential.

DPAPI authenticates ciphertext but does not prevent rollback to an older valid blob.
A sound backup/rollback/tombstone model and explicit reset/re-pairing behavior must
be approved and tested before implementation. No claim of rollback-proof persistence
is made. Required native tests remain: corruption/unprotect failure, interrupted
replace, restart, revoke/rotate durability, key loss, ACLs, bounded input, and safe
cleanup of isolated test keys/certificates. They were NOT implemented or executed
as though DPAPI/key protection already existed.

## Future local-Wi-Fi listener / Tailscale boundary

Only after approval/proof: obtain authoritative Windows NLM profile for the EXACT
assigned NIC/address; private-looking IP is insufficient. New read-only helper leaves
that profile UNKNOWN until a verified NLM adapter exists. Subscribe to address/profile/
interface changes, stop/cancel exposure on mismatch and require fresh approval. Do
not mutate profiles, use wildcard bind, open firewall/router ports, expose commands,
or proxy G HUB. Serve immutable cached snapshots only; no HTTP action can poll devices.

Enforce connection/TLS/header/body/idle/cancellation/shutdown budgets BEFORE the request
core. IsTls must derive from a successfully authenticated/encrypted real TLS transport,
not client-supplied flags. Public certificate matching here is not that transport.
No cleartext/trust-all/redirect fallback. Preserve current request quotas and response
bounds; no app networking is introduced in this stage.

Tailscale is later: an explicitly approved interface/address policy change is needed
(the current RFC1918/ULA gate rejects its usual100.64/10 range). Validate actual
interface and owner approval; membership/address is never authentication. Keep the
same pinned TLS/per-phone bearer rules. No VPN install/configuration occurred here.

## Verification and exact inventory

Execution environment: Linux, .NET SDK10.0.403. Locked restore PASS. Release solution
cross-build PASS,0 warnings/errors. Focused portable51 passed; final full portable
suite613 passed,0 failed/skipped, including11 new certificate cases. Existing policy,
revocation, quota, shutdown, default-off and battery regressions passed. Four NEW
Windows read-only tests compiled; Windows-native tests overall were NOT RUN. Native
CNG/ACL/DPAPI, real TLS/pinning, trust stores, profile transitions, physical devices,
tray/clipboard, LAN/Wi-Fi/phone/Tailscale and provisioning/storage tests NOT RUN.

Commands: dotnet restore PeripheralBatteryCompanion.sln --locked-mode;
dotnet build PeripheralBatteryCompanion.sln -c Release --no-restore;
dotnet test tests/PeripheralBatteryCompanion.Tests/PeripheralBatteryCompanion.Tests.csproj
-c Release --no-build --no-restore. Schema-byte/protected-file/Android audits PASS.

Five NEW files only:
- src/PeripheralBatteryCompanion.Core/TransportSecurity/PublicServerCertificateInspector.cs
- src/PeripheralBatteryCompanion.Windows/ReadOnlyWindowsSecurityInspection.cs
- tests/PeripheralBatteryCompanion.Tests/PublicServerCertificateInspectorTests.cs
- tests/PeripheralBatteryCompanion.Windows.Tests/ReadOnlySecurityInspectionTests.cs
- docs/STAGE_A2_2C3_READONLY_NATIVE_INSPECTION.md

## Owner checklist / stop gate

Review the inspection-only stacked PR and explicit limitations. Run
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\test.ps1 -Release -CopySummary
on Windows and provide actual summary; do not launch the tray for these tests or
initiate device/LAN queries. Native tests only read existing metadata and install no
certificates/keys. Confirm missing identities remain missing and private profile/
key protection are not guessed. Approve/revise the proposed SAN/port/lifetime/UI/
rotation choices above before provisioning. Arrange Windows execution of future
isolated CNG/ACL/DPAPI fixtures before any such implementation can be called safe.
Stop before listener, pairing activation, Android integration, merge or release.
