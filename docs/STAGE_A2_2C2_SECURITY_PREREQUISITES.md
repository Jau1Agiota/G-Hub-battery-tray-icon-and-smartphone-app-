# Stage A2.2C2 — security prerequisites, non-listening/default-off

## Decision record and baseline

Implement only portable, fail-closed prerequisite contracts and an isolated CSPRNG
primitive. No native security adapter is implemented, registered or activated.
Authentication choice and certificate lifecycle/SAN policy require owner approval;
unspecified choices are rejected, not selected by code. No port can be opened by
any component in this stage. A consistent evidence result is not TLS verification.

Base: A2.2C `d7092d1b7dd4887a34f2fd0ca8acef8933dff567`, PR9 (open/draft when
inspected), itself stacked on A2.2B PR8 `c6971071e541099cf1b8e8cfef0b4ee1d3a1c1dc`.
This branch/PR stacks directly on A2.2C; no prerequisite merge or duplicated base diff.
AGENTS.md, both PR states, A2.2B/C reports, schema and production lifecycle/settings
were reviewed. Owner-reported A2.2C Windows baseline: .NET10.0.402, Release restore/
build PASS,599 tests passed,0 failed/skipped, exit0. Tray and hardware were NOT tested.
That is owner evidence, not a Windows execution performed by Codex.

## Implemented, with no production wiring

- SecurityContracts: immutable opt-in, approved endpoint/auth choice, approval window,
  transport budgets and typed native-evidence contracts. No actual certificate,
  credential-store, interface inspection or listener implementation is provided.
- LanApprovalSession: default OFF, with no evidence call before explicit local owner
  approval. A persisted desired ON flag cannot restore session approval. Consent,
  dates, explicit auth selection, exact canonical local address/interface/port/SAN/
  pin, Private profile, key availability/protection and protected enrollment storage
  are required. Unknown/failed observations deny; no exceptions/details leak.
- Only RFC1918 IPv4 and IPv6 ULA addresses are accepted. Wildcard, public, loopback,
  multicast, link-local, IPv4-mapped, scoped and noncanonical forms are rejected.
  Port must be explicit and unprivileged1024..65535; no default port is assigned.
  The initial restriction is conservative; link-local/privileged-port support needs
  separate review. A private prefix alone NEVER substitutes for profile/assignment.
- Expiry is bounded by UTC approval/certificate windows AND a monotonic session
  budget. Moving the wall clock backward cannot extend a grant. Any failed profile,
  interface, key, identity, storage or time check drops the active approval. Restoring
  evidence alone never re-enables it. Disable clears approval; shutdown is terminal.
- Ephemeral256BitSecret.Create uses RandomNumberGenerator.GetBytes(32). No secret is
  created at startup. Explicit CopyTo and SHA256-verifier APIs support future reviewed
  enrollment; ToString is redacted and disposal zeros the owned buffer. Callers must
  clear exported copies. Managed-memory copies cannot be claimed globally erased.
  No token persistence, issuance endpoint, bootstrap session or pairing is implemented.

The existing C auth registry/core remain unchanged and default-off. Tests demonstrate
an explicitly created secret can authenticate the in-memory registry and be revoked;
this is not pairing, actual TLS or secure persistent storage. Approval APIs are for
future trusted LOCAL owner UI code; a bool/evidence record does not prove a human's
consent or native platform security. Never bind these APIs or evidence flags to
remote JSON/HTTP input. No adapter starts/stops a real listener in this stage.

## Production audit / integration seam

TrayApplicationContext owns monitor startup, caches and UI dispatch; OnUpdated/Display
retains Logitech state, DisplayHyperX reads immutable Current after existing events,
and CloseAsync joins query/schedule/monitor work before disposal. These files are
byte-identical. The A2.2C publisher is still unregistered. Future security/transport
integration must use only its cached publications, not Refresh or provider calls.
Network change notifications would have to trigger policy re-evaluation and actual
transport shutdown; this policy does NOT watch Windows networks or enforce live
socket closure. Evaluate explicitly rechecks injected evidence and latches failures.

HyperXPreferencesStore and theme JSON are not credential stores. Never put LAN
secrets there or reinterpret existing monitoring consent as LAN approval. Shutdown/
disable must stop future transport and invalidate approval independently, without
resuming/stopping/reconfiguring hardware monitoring. Schema v1 and timestamp/null/
precision/HyperX ambiguity semantics are unchanged; Android is untouched.

## Native implementation plan — deferred and owner-gated

API availability was inspected: the installed .NET reference packs include
System.Security.Cryptography.Cng; WindowsDesktop references include ProtectedData.
No dependency was added. Available APIs are not proof that native deployment is safe.

Certificate/key proposal: a per-installation anonymous DNS authority (NOT machine
name), explicit SAN, ECDSA P-256 signing key in CurrentUser CNG/KSP with no export
policy, user-restricted ACLs, and CurrentUser/My certificate association. Future code
must verify persisted nonexportability, key association/access rights and signing
availability on Windows; never export PFX or silently weaken protection. Do not
install a system-wide root. APIs to evaluate include CngKey/CngKeyCreationParameters,
CertificateRequest and X509Store. No certificate/key was created in this stage.

Pin design: SHA256 of DER SubjectPublicKeyInfo (certificate.PublicKey.
ExportSubjectPublicKeyInfo), matching Android publicKey.encoded; not a raw-key or
certificate-fingerprint substitute. Android must verify explicit trusted pin/anchor,
SAN/hostname and validity; no trust-all manager, cleartext exception or pin fallback.
Manual private IP entry may map a stable certificate authority through a dedicated
client resolver; address changes must never change trust automatically.

Owner must choose validity (e.g.90days versus1year), renewal/rotation UX and stable
SAN/address policy before code. Missing key, reinstall or changed SPKI means OFF and
fresh explicit approval/re-pairing, not automatic replacement. Even same-key renewal
needs a reviewed native certificate-selection policy; SPKI matching alone is not
that lifecycle. Keep revocation tied to server identity generation across restarts.

| Auth option requiring owner choice | Benefits | Costs / remaining obligations |
| --- | --- | --- |
| Pinned TLS + per-phone256-bit bearer | Fits current verifier boundary; smaller enrollment surface | Stolen-token replay until revocation; protect delivery/storage; validate every request |
| Pinned TLS + mutual TLS | Sender-constrained private key, Android Keystore integration | Client certificate enrollment/expiry/revocation; more PKI/Schannel/native validation |

No mode is deployed or implicitly chosen: Unspecified denies approval. Current request
core only models bearer verification; accepting MutualTls as a policy option does
NOT implement mTLS authentication. Future adapter support must match owner choice.

Enrollment proposal: owner opens a short local-only QR window (proposed2minutes,
5 attempts), with pin/authority and one-time CSPRNG bootstrap proof. Phone validates
TLS identity before submitting proof. Explicit Windows approval enrolls one phone;
consume bootstrap atomically, reject replay/expiry, bound attempts and avoid logs/
URLs/clipboard secrets. These window/consumption rules are design only in this stage.
Per-phone rotation/revoke/re-pair must invalidate old credentials immediately for
new requests and cancel applicable live sessions; already-disclosed bytes cannot
be revoked. Android key/token handling remains later owner-reviewed work.

For bearer enrollment, Windows needs only the high-entropy token verifier; protect
registry integrity and any bootstrap state using DPAPI CurrentUser in a separate
app-security store with appropriate user ACLs and atomic updates. mTLS needs protected
CA/server keys and an approved-client/revocation registry. Do not use Machine scope,
plaintext monitoring JSON, log secrets or silently reset a corrupt/unprotectable
store. DPAPI/CNG behavior must be tested on Windows before actual use. No storage
files, OS keys, certificates or enrollment records were written by this stage.

Binding proposal: owner selects one actual assigned private address/interface; native
adapter confirms Network List Manager Private category plus interface/address/up
state. No automatic choice of another NIC, wildcard binding or public/Domain trust.
On address/profile/interface change, stop exposure and require fresh approval; no
implicit firewall change. No Windows network/profile API was exercised here.

Transport budgets are typed/reviewable but NOT enforced on sockets: <=16 connections,
<=5s TLS/header deadlines, <=8192 header bytes,0 body bytes, <=10s idle, <=64KiB
response and <=5s graceful shutdown before controlled cancellation. A future native
adapter must enforce these BEFORE request-core entry and must not treat IsTls as
client evidence. Existing global60/min and per-phone10/min request quotas are not
protection against TLS/slow-header exhaustion. TLS1.2 minimum/TLS1.3 preferred,
no0-RTT/cleartext/redirect fallback and no remote command/refresh/pairing routes.

## Concise threat model / unresolved decisions

Rogue LAN clients and credential guessing: authenticated TLS, strong per-phone auth,
quotas/revocation. MITM/DHCP spoofing: physical pin bootstrap and verified authority.
Key/token theft: CNG/Keystore/protected storage, bounded validity and re-pairing.
Profile drift: fail closed, no implicit NIC fallback/firewall opening. Malformed/slow
requests: pre-core transport limits. Local same-user malware/admin compromise is not
solved by DPAPI/policy flags. Fake evidence, compromised producer or mislabeled demo
records are outside this policy's attestation; never claim hardware-source proof.

Owner decisions required before native/listener work: auth option; SAN/address/port;
certificate validity/rotation and native key/storage lifecycle; opt-in persistence and
session approval UX; one-time bootstrap/revocation UX; transport budgets/interface
profile enforcement. This branch deliberately stops at contracts/primitives, rather
than guessing these material deployment choices. Request owner review before code
that provisions identity, persists secrets, enrolls phones or exposes a network port.

## Verification, file inventory and checklist

Executed: locked solution restore, Release cross-build (0 warnings/errors), focused
run67/0/0 before the final rollback test, and final full portable suite602 passed,
0 failures/skips.40 cases are new security prerequisite cases. Existing malformed-
credential/quota/revocation/shutdown tests passed in the full suite. Windows-native
assemblies compiled, NOT executed. Platform evidence tests use fakes, not actual
CNG/DPAPI/network profiles. Real TLS/pinning, native security, pairing/LAN, tray,
physical HID/gateway/helper, clipboard and Android/device tests NOT RUN.

Commands: `dotnet restore PeripheralBatteryCompanion.sln --locked-mode`;
`dotnet build PeripheralBatteryCompanion.sln -c Release --no-restore`;
`dotnet test tests/PeripheralBatteryCompanion.Tests/PeripheralBatteryCompanion.Tests.csproj -c Release --no-build --no-restore`.
Schema byte comparison and protected-file/Android audits PASS. No old production
file/dependency/permission or snapshot JSON field was changed. Five new files only:
- src/PeripheralBatteryCompanion.Core/TransportSecurity/SecurityContracts.cs
- src/PeripheralBatteryCompanion.Core/TransportSecurity/LanApprovalSession.cs
- src/PeripheralBatteryCompanion.Core/TransportSecurity/Ephemeral256BitSecret.cs
- tests/PeripheralBatteryCompanion.Tests/TransportSecurityPrerequisiteTests.cs
- docs/STAGE_A2_2C2_SECURITY_PREREQUISITES.md

Owner checklist: review the stacked PR, choices and fail-closed tests; run
`powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\test.ps1 -Release -CopySummary`
and share actual results. Do not launch the production tray or initiate hardware/
LAN probes for this stage. Confirm no native adapter/listener/startup wiring exists.
Approve the listed deployment decisions separately before any later native/listener
phase. No Android APK is expected; no merge/release or pairing activation occurred.
