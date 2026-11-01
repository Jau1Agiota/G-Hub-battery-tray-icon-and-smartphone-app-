# Proposed isolated Windows acceptance batch — NOT AUTHORIZED / NOT EXECUTED

This is one resource-bounded proposal for review, not blanket Gate E approval.
Production execution, `W1NativeLifecycle.Execute`, listeners, pairing/enrollment,
Android connectivity and Stage E4 remain unauthorized. No existing hold, Skip,
permit restriction or provenance check is disabled by this proposal.

## Prerequisites and isolation

The isolated driver source now exists in `tools/GateE.NativeAcceptance/`, but
entry and case admission remain unconditionally BLOCKED. See the exact
[execution manifest](gate-e-native-execution-manifest.md). No native case ran.
Before execution, separately review its source, exact
commit, binaries/dependencies and hashes, plus a completed resource manifest and
the selected cases below. A later owner decision must explicitly authorize that
revision and those operations/resources. Do not implement a token, reflection
escape or environment switch around current unconditional guards.

The reviewed driver must exercise the SAME production native primitives through
explicit test-only admission, leaving production factories and W1 holds denying.
Shared-helper extraction/admission changes need independent source review before
any native run; a reimplementation or mocked driver cannot attest the backend.
Until then all native cases below remain blocked, including signature/entropy.
Production source is linked into the isolated executable with a compile-time test
admission branch; normal production builds retain unconditional guards and reject
that symbol. No production startup references the harness. Failure seams exist
only in its storage wrapper. The full fault plan retains its uncertain evidence;
it does not automatically invoke the compiled exact-inventory cleanup path.

Use one disposable local Windows VM/snapshot, a nonadministrator test account and
fixed NTFS volume. No real user profile/app data, certificate stores, devices or
production credentials. Record OS build, architecture, runtime/provider versions,
filesystem, binary hashes and exact source revision. Restore/build managed code
before the batch; no CI, concurrent tests or other testhost. Run each native case
in a fresh child process with fixed case ID, resource manifest and 30-second
deadline. Parent observes retained process handles/PID/parent/start/exit; parent
must not load the test assembly. No broad process-name kill or key enumeration.

The manifest must bind:

- One fresh run UUID, explicit test-account SID and resolved absolute local root
  `<LocalAppData>/PBC.GateE.Acceptance/<run-uuid>`; refuse any pre-existing root.
  Pin/validate ancestors before creation. No elevation or ACL changes to ancestors.
- At most 24 independently named journal fixture directories under that root,
  each exclusively owned, nonreparse and limited to the existing nine journal names
  and 64 KiB. State ciphertext <=16 KiB; PBCJNL02 plaintext <=12,352 bytes;
  authorization JSON <=8,192 and replay metadata <=4,096 bytes. No real telemetry.
- One exact newly generated fixture identity UUID and key name
  `PBC.Identity.<fixture-identity-uuid>` in CurrentUser Microsoft Software KSP.
  Refuse an existing exact name; never enumerate, overwrite or open application
  keys. At most ONE persistent P-256 signing key, ONE in-memory public certificate,
  and ONE public DER file <=4 KiB outside the journal directories.
- A `sentinel/` fixture and path/ACL/link fixtures entirely inside the run root;
  no link target outside it. Sanitized evidence <=1 MiB, no bodies, bootstrap,
  bearer, DPAPI plaintext/ciphertext, private key or Authorization header dumps.
  ONE `secrets/fixture-credential.bin` DPAPI ciphertext file <=16 KiB may be
  created by the separately reviewed fixture protected-store sink, outside journal
  inventories; no plaintext file, backup or production secret storage is permitted.
- Maximum 24 child processes, 128 DPAPI transforms, eight fixture-key signatures,
  and 16 public signature verifications. No network connection, listener, trust
  store changes, hardware access, provider fallback or automatic retry.

## Exact case operations and required evidence

| Case group | Operations within manifest | Required observation / rejection |
| --- | --- | --- |
| D: CurrentUser protection | Protect/unprotect bounded synthetic record and PBCJNL02 envelope with the actual binding entropy; mutate ciphertext, substitute identity/endpoint/leaf binding, test zero/oversize bounds. Keep raw inputs memory-only and zero temporary buffers. | Exact round trip only in correct context; corruption/wrong binding denies without plaintext result or fallback. CurrentUser scope is not proved by same-user success. |
| D-user: wrong user | Only if a second disposable account/SID and its process launcher are explicitly added to the reviewed manifest: copy ONE owned ciphertext fixture into a separate owner-created handoff directory; run one unprotect attempt under that account. No saved password/credential provisioning by the driver. | Wrong-user rejection, no returned plaintext; remove only manifest-owned handoff files. Without this additional named-account approval mark NOT RUN, not passed. |
| K: custody | Create the one exact named CurrentUser P-256 Software-KSP key, signing-only/export None, requested owner-only descriptor; read provider/scope/usage/export/descriptor. Close and reopen that exact name, compare exported PUBLIC SPKI, reject substituted expected identity/SPKI. | No overwrite/machine/fallback path; metadata/readback match. Descriptor mismatch or partial creation is uncertain. Export-policy metadata alone is not empirical proof that every private-export API denies. No private-export attempt is in this batch. |
| C: certificate association | Using the fixture key only, issue one self-signed, non-CA P-256/SHA-256 leaf, DigitalSignature/serverAuth, one fixed private IPv4 SAN used only as data (no binding), validity <=24h. Keep certificate private-key association in memory; write public DER only. Compare SPKI and exact enrolled DER; sign one fixed public challenge with the exact reopened fixture key and verify using the leaf public key. | Strict profile and real self-signature verification succeed. Changed signature, wrong SAN/pin/leaf/usage/algorithm/validity and substituted public challenge deny. No OS trust installation, certificate store, real enrollment or TLS handshake. Record association/challenge result without signature/private material dumps. |
| E: entropy | Up to eight 32-byte request nonces, ONE bootstrap delivery to memory-only sink, ONE credential to fixture CurrentUser DPAPI storage before returning its verifier. Dispose/zero raw buffers and ephemeral bootstrap immediately. | Length/canonical encoding and delivery-before-verifier; sink failure returns no verifier and no retry. Uniqueness samples do not prove entropy quality. Store failure may have persisted data: retain as uncertain, do not assert rollback. No credential is registered or used. |
| F: paths/ACL/links | Create root/children with actual protected owner-only descriptor. Inspect actual handles/paths/volume/links. In separate empty fixtures only: broad/null/inherited/foreign-owner ACL, reparse target and two-link file. Pure invalid path strings include relative/UNC/device/ADS aliases; reject before external access. Do not change the account owner of real files or require extra privileges. | Actual backend refuses each unsafe fixture before state access. Link/junction creation unsupported without elevation => NOT RUN for that case. Never relax ACL/volume checks to pass. Record only bounded case/outcome/handle metadata. |
| F-flush: journal | Actual create/write/Flush(true), same-directory replace, directory FlushFileBuffers, readback and clean close/reopen; initial witness, session and pending intent, active record/replay CAS and tombstone. All synthetic, no exposure. | Supported native flush succeeds or batch blocks on unsupported platform; no skip-to-success, volume-level fallback or privilege escalation. API success is not power-loss proof. |
| R: crash/restart | With test-only phase hooks in the shared implementation, terminate ONLY the exact owned child after: initialized witness; pending flush; state replacement; checkpoint replacement; pending removal; session removal. Use a distinct fresh journal for each. Reopen in a fresh observed process; separately exercise normal clean reopen and committed tombstone/replay restart. | Incomplete/abandoned evidence denies exposure and latches uncertainty, never missing-to-empty initialization. Clean committed state/replay survives only expected clean close; tombstone cannot reactivate. A kill after actual clean session removal may classify clean, which must be recorded. Full consistent rollback is NOT detected/protected. |

Crash hooks and fault injection must be explicit reviewed test seams, never
production environment flags. Bound write/replace/flush/close failure injection
to owned fixtures; stop on the first unexpected native failure. Real abrupt
process death can be observed; host/VM power loss is outside this batch and must
remain NOT RUN. No journal scope may be reopened by two children concurrently.
The association challenge is the exact ASCII `PBC-GATE-E-ACCEPTANCE-1` plus LF,
the manifest run UUID in lowercase D format plus LF; no user content is signed.
The SAN fixture is `192.168.100.10` as certificate DATA only, never an address to
bind/connect. Certificate issue/expiry times are whole UTC seconds fixed in the
approved manifest, no later extension or renewal.

## Cleanup and failure handling

Always fence/cancel application work first (there is no exposure in this batch),
close owned streams/handles, and wait for exact child exit. At deadline, request
termination only through the verified owned process handle, then observe exit.
Unknown exit => stop, retain resources and report cleanup UNCONFIRMED. Do not
delete a directory/key underneath an unconfirmed process.

On a successful case, delete only resources proven newly created by this run:
the exact fixture key name under the test account, the exact in-memory certificate
and public DER, owned handoff/link fixtures, and manifest-listed files/directories.
Verify the resolved root and every delete target remains within the manifest,
reject reparses, remove links themselves before targets, and verify exact key
absence/root absence. No wildcard key deletion, recursive traversal of a reparse,
certificate-store cleanup, reset/clean of this repository or collateral user-file
cleanup. Native fixture-key deletion requires explicit approval in the same
manifest; existing custody source deliberately has no delete operation.

On failed association, partial key creation, persistence/close/flush failure,
uncertain process exit or unverifiable ownership, preserve exact resources and
session/intent/stop evidence. No automatic delete, retry, reinitialize or recovery.
Provide bounded identifiers/outcomes for owner review; cleanup is NOT successful
merely because Dispose/Terminate returned. Any containment breach stops the batch.

## Acceptance evidence and boundaries

Produce one case table with PASS/FAIL/SKIPPED/NOT RUN/UNCERTAIN kept distinct,
sanitized per-process start/exit evidence, actual native error codes, supported
provider/flush observations, restart classification and verified cleanup/retained
resources. No secrets in clipboard/logs/PR artifacts. Do not describe mocks,
metadata-only assertions, simulated failures or a limited polling trace as native
security proof. Document any unavailable OS-event tracing explicitly.

The batch is not complete Gate E acceptance. It excludes real TLS handshakes,
Android Keystore and transport proof, active enrollment, production credential use,
network/interface/profile changes and real power loss. The Windows adapter checks
an actual bound stream's TLS 1.3 / HTTP/1.1 properties in source, but runtime
resumption/0-RTT/options, platform possession and cancellation/lifetime remain
unvalidated. A future transport stage requires its own narrowly reviewed scope;
no Stage E4 permission is implied. Owner acceptance is always a separate decision.
