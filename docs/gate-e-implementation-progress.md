# Gate E implementation — protected execution remains blocked

Baseline: `main` at `45361d0dc8d3c609c28a7abdf2929b7a0c244ced` (merged PR #32).
Git ancestry confirmed PR #30 head `8b31236`, PR #31 merge `4d12a5b`, and the
integration commit `0e0a806` are ancestors. Branch: `codex/gate-e-security-implementation`.
The recorded preceding regression baseline was 973 passed, no failed/skipped.
Completed Gate B investigations were not repeated.

## Implemented, without production activation

The source of truth remains E3.1 sections 4, 6, 7 and 12. This change does not
replace the persisted CurrentUser CNG/DPAPI + Android Keystore architecture.

- `EnrollmentIdentity` enforces canonical HTTPS RFC1918 IPv4 endpoints with explicit
  port and no DNS/path/query/fragment/userinfo. It defensively retains the exact
  public leaf, SPKI digest, identity generation and validity binding. **The type is
  binding data, not proof that a certificate, key or TLS peer is valid.** It accepts
  synthetic bytes in tests and must not be used as a certificate verifier.
- `EnrollmentClaim` accepts only canonical 32-byte base64url nonces, UUIDs and
  lowercase SHA-256 verifier strings. It serializes the exact seven-line E3.1
  transcript with final LF and calculates the full public transcript digest.
  It never creates or stores a bearer, bootstrap secret or native approval token.
- `AuthorizationRecordCodec` implements a bounded, versioned verifier-only storage
  payload: exact identity/leaf/context, phone, enrollment, generation, scope,
  issue/expiry and tombstone. It rejects missing/unknown/duplicate/escaped-duplicate
  keys, malformed UTF-8, BOM, nesting, invalid types, noncanonical GUID/base64,
  oversized input and identity substitution. Input is snapshotted before parsing.
  The storage encoding is an implementation detail, not a new wire protocol.
  **These bytes are plaintext until a future approved protected-store adapter uses
  them. No file is written by the codec.**
- `EnrollmentAuthorization` implements one-claim, 120-second owner-opened windows,
  exact transcript confirmation, one active phone, compare-generation commit
  ordering, conservative certificate-capped issuance lifetime, monotonic and UTC
  checks, and fresh session-only exposure approval. Restored records never enable
  exposure. Commit errors/unknown outcomes latch recovery with no reset/retry API.
  Generation exhaustion reserves space for retirement rather than wrapping.
- Revocation invalidates prepared response leases **before** waiting on storage.
  Acknowledgement requires a committed tombstone; uncertainty stays denied. A
  committed approval returning past its deadline is never exposed and triggers one
  retirement commit, not a retry of issuance. Lease rechecks reject foreign owners,
  stale phone/generation, revocation, expiry and clock regression. They are release
  eligibility checks for an already authenticated future adapter, **not bearer or
  TLS authentication**, and cannot recall delivered bytes or cancel network writes.
- `WindowsCurrentUserRecordProtection` supplies DPAPI protect/unprotect source,
  bounded buffers, exact context entropy, UI-forbidden/CurrentUser flags, pinned
  input ownership, attempted output wiping/LocalFree, and separate operation versus
  cleanup-uncertainty failure bits. Every public-to-assembly operation starts with
  an unconditional throw-only Gate E hold. No enable flag, permit, reflection hook,
  startup registration or fallback exists. **Native implementation source was
  compiled, but none of its native paths was executed or validated.**

CurrentUser flags, optional-entropy binding and LocalFree ownership follow
[Microsoft's CryptProtectData contract](https://learn.microsoft.com/en-us/windows/win32/api/dpapi/nf-dpapi-cryptprotectdata)
and [CryptUnprotectData contract](https://learn.microsoft.com/en-us/windows/win32/api/dpapi/nf-dpapi-cryptunprotectdata).
They do not establish ACL protection, rollback resistance or verified cleanup.

The initial `d7ae87d` milestone supplied a journal interface and fake ordering tests.
The subsequent durable-journal milestone below adds the real file transaction
implementation and held Windows production composition. Missing storage is never
interpreted as new trust. Native durability and custody still require protected
validation; constructing another coordinator is not legitimate recovery.

The existing DNS-oriented `LanApprovalSession`, in-memory phone registry, snapshot
request core, monitoring, UI and startup remain unchanged. The new IPv4 identity
must not be combined with the old DNS profile to claim production eligibility.
There are no startup/UI/network call sites. The held Windows journal composition
now connects the coordinator and native adapter internally, as described below.

## Initial milestone verification (historical)

Windows x64, .NET SDK 10.0.402. No dependency or framework changes.

| Check | Genuine result |
|---|---|
| Initial targeted managed tests | 54 passed, 0 failed/skipped before final edge/Windows-boundary cases |
| `./test.ps1 -Release` | Restore PASS; Build PASS; **1,032 passed, 0 failed, 0 skipped**, 2/2 TRX files; exit 0 |
| New cases within final regression run | **59 passed** (58 managed, 1 Windows denial-boundary case); not additional to 1,032 |
| Existing Gate B verifier, sequential | **15 passed, 0 failed, 0 skipped**; discovery 8 -> 15; exit 0 |
| Diff/source inspection | Original W1 native Skip, approval/provenance/report holds, fail-closed permits, test runner and CI unchanged |
| Native CNG, DPAPI, certificate, file custody or TLS | **NOT RUN** |
| Android build/tests | **NOT RUN**: no Android Gradle project/wrapper exists in this checkout; no Android source changed |
| Real Windows tray/device/Windows–Android integration | **NOT RUN** |

New regression coverage includes malformed record matrices, full leaf substitution
with matching pin, exact transcript bytes, canonical nonce rejection, uncertain and
throwing commits/loads, independent clock expiry/regression, late-commit retirement,
generation exhaustion, fresh exposure, and a synchronized blocked-persistence test
showing immediate lease denial on revocation. Synthetic leaf bytes and fake durable
outcomes are not native/security acceptance evidence. The Windows boundary test
passes null inputs and observes the unconditional hold **before** any serialization,
DPAPI, key or certificate operation. No protected test was unskipped or run.

Reproduction (ordinary checks only):

```powershell
dotnet test tests/PeripheralBatteryCompanion.Tests/PeripheralBatteryCompanion.Tests.csproj --no-restore --configuration Release --filter 'FullyQualifiedName~PeripheralBatteryCompanion.Tests.EnrollmentAuthorizationTests|FullyQualifiedName~PeripheralBatteryCompanion.Tests.AuthorizationRecordCodecTests' --logger 'trx;LogFileName=gate-e-managed.trx' --results-directory test-results/gate-e-managed --verbosity minimal
./test.ps1 -Release
# After ordinary tests finish, on a clean checkout with the reviewed verifier:
./tests/PeripheralBatteryCompanion.W1Offline.Tests/GateB/verify.ps1
```

Raw local evidence: `test-results/summary.txt`, `test-results/automated/*/results.trx`,
`test-results/gate-e-managed/gate-e-managed.trx`, and Gate B session
`test-results/e3-3-gate-b/20261101T191349382Z/`. Review logs before sharing.
The local unrelated correction in GateB/verify.ps1 was preserved/excluded; Gate B
was run from a temporary adjacent copy extracted from committed source, then
removed. The four unrelated untracked files were also preserved/excluded.
No new test failures occurred in these implementation checks. No CI result is
claimed here, and no complete Gate B audit was rerun.

## Remaining acceptance blockers and protected plan — NOT AUTHORIZED

- [x] Reviewed E3.3 ancestry restored in main; new branch is based on that main.
- [x] Actual binding/serialization and enrollment/retirement ordering implemented.
- [x] Permitted regression and Gate B smoke checks pass; native holds unchanged.
- [ ] Native adapter correctness, servicing provenance, user scope and ownership.
- [ ] Native validation of the implemented durable journal: restrictive ACL/ancestor/
  reparse/path-to-handle checks, atomic replacement/flush, generation/tombstone and
  restart safety. Managed transaction/stop-marker cases pass; native guarantees
  remain unverified. Full consistent rollback remains unsolved; no plaintext fallback.
- [ ] Authenticated exact-operation native permits, trusted atomic one-time nonce
  consumption, executable/runtime provenance and verified report destinations.
  Existing W1 acquisition/consumption still always deny.
- [ ] Native validation of persisted nonexportable CurrentUser Software-CNG P-256
  custody source, plus certificate/TLS association. Existing **ephemeral** W1 source
  cannot substitute for the persisted identity specified by E3.1.
- [ ] Actual leaf/signature/IP-SAN validation, bootstrap generation/authentication,
  approved claim/status framing, credential issuance receipts and peer/bearer
  authentication. The binding types and public transcript hash do not supply them.
- [ ] Durable 64-instance replay history, restart high-water marks and nonce
  correlation; cancellation of real in-flight/queued writes. Generation leases
  alone are not full replay resistance.
- [ ] Android Keystore access policy/implementation, protected-record binding and
  actual Android scoped trust-anchor/TLS-stack proof. No replacement policy chosen.
- [ ] Independent review and owner acceptance; production activation stays unavailable.

The next protected revision must be reviewed **before** any hold/Skip change. Its
approval packet must name exact source/binary/runtime/provider hashes, isolated
user/session, bounded operations/deadlines, new owned resource names/destination,
stop/cleanup handling and sanitized output. This PR intentionally provides no
command that executes past a native hold. Proposed acceptance cases, not permission:

| Separately authorized case | Required evidence |
|---|---|
| DPAPI same-user record roundtrip | Exact record equality, scope/context binding and bounded outputs; no UI |
| Wrong-user, corruption or changed entropy/context | Denial without fallback, activation or sensitive diagnostics |
| Protection failure and each owned-buffer release failure | Primary failure retained, cleanup uncertainty reported; no claimed native deletion from managed Dispose |
| Protected journal interruption/restart | No active publication before durable commit; denied uncertain/missing state; durable revocation cannot resurrect |
| Persisted CNG identity and public certificate | Signing-only P-256, nonexportable/current-user ACL, exact association and profile; no broad enumeration, export, deletion or automatic retry |
| Standalone W1 experiment | Existing native test remains skipped; a new reviewed invocation must reconcile its old no-permit/report assumptions before any one-shot approval |
| Android storage/trust proof | Separate Android checkout, selected Keystore policy, crash/restore/invalidated-key rejection, exact scoped anchor and pin checks |

Owner review must settle resource ownership/ACL and native failure-supervision
details before those operations. Neither fake COMMITTED outcomes nor these passing
unit tests settle platform custody or authorize native use. Full consistent storage
rollback remains an explicit E3.1 assurance limit unless an independent trusted
anchor is separately selected and reviewed. No new anchor or trust model is invented.

Stage E4 listeners, live enrollment, networking integration and all protected Gate E
execution remain BLOCKED and UNAUTHORIZED. This is a substantive **partial** Gate E
implementation PR, not completion or acceptance of production security.

## Durable journal and persistent custody milestone (2026-11-01)

Pre-change baseline: `d7ae87d`, Release 1,032 passed / 0 failed / 0 skipped.
This milestone implements production journal architecture rather than replacing
the persistence interface with another in-memory fake. Production construction
still unconditionally denies **before** filesystem/security/native operations.

### Actual file protocol and coordinator integration

`DurableAuthorizationJournal` implements `IAuthorizationJournalSession` and the
existing `IAuthorizationJournal` contract. `AuthorizationJournalFileStorage` uses
real exclusive FileStreams, bounded reads, create-new writes with WriteThrough and
`Flush(true)`, same-directory replacement, readback and mandatory directory flushing.
Windows supplies non-following handle opens, path/ACL validation and native flush;
these are mandatory services, with no production no-op/fake option exposed.

The fixed inventory has nine allowed names, maximum total 65,536 bytes, individual
ciphertexts <=16,384 bytes and plaintext envelope <=8,256 bytes. It contains:

- `lease.lock`: lifetime exclusive session lock; never used as an approval token.
- `initialized.bin`: exact identity-binding digest, created once before initialization;
  never removed by the journal.
- `state.bin`: DPAPI-protected versioned envelope, either initialized-empty or one
  authorization/tombstone. Exact leaf/context and generation are encoded inside.
- `checkpoint.bin`: generation plus SHA-256 of exact ciphertext; detects mismatched
  state/checkpoint pairs, not hostile full-state rollback or standalone authenticity.
- `pending.bin`: write/flush before changing state; state/checkpoint temporary files
  (`state.next`, `checkpoint.next`) are then flushed, replaced and read back before
  pending deletion and directory flush can acknowledge COMMITTED.
- `session.bin`: persisted before a verified load/initialization is returned; retained
  for the coordinator lifetime. Abandoned session, transaction or temporary markers
  deny restart. Only a clean, non-poisoned close may remove the session marker.
- `stop.bin`: persistent uncertainty evidence, never automatically removed. Failure
  attempts to persist this without overwriting earlier evidence; if writing it fails,
  existing session/intent markers are retained.

Missing files never become VerifiedEmpty automatically. Owner initialization is
separate, and the Windows factory requires **CreateDirectory on a new exact path**;
it does not reset/reinitialize an existing directory. Partial initialization denies.
CAS and transition checks reject stale generations, active-phone replacement,
tombstone binding substitution and reuse of the immediately retired enrollment ID.
No automatic retry, rollback-to-old-copy, marker cleanup, or trust recovery exists.

The coordinator now owns/disposes session journals. It invalidates release eligibility
before close; failures latch recovery and call MarkUncertain, including clock failure
before the first journal Read. Clean shutdown retains credentials/tombstones but
clears only the liveness marker. Reopening never restores exposure. Close failure
is reported rather than silently described as clean. The Windows composition offers
a held coordinator factory, with no UI/startup/listener registration.

### Held Windows backend and CNG foundation

The production `WindowsAuthorizationJournal` combines this real file protocol with
`WindowsCurrentUserRecordProtection` envelope operations and a pinned private root.
`WindowsAuthorizationDirectory` source restricts the initial scope to fixed NTFS
volumes and canonical bounded drive paths, rejects UNC/device/ADS/relative/DOS alias
forms, pins every ancestor without delete sharing, and uses OPEN_REPARSE_POINT for
directory **and file opens**. Disk type, final handle path, no reparse and one file
link are checked before data access. Exact owner-only DACL checks reject absent/null,
extra/broad/deny/inherit-only access. Fresh root creation supplies the owner-only
protected descriptor at creation; children must inherit only that owner access.
No unowned directory entries are read as state or removed.

These are **source implementations, not observed native security properties**.
Directory FlushFileBuffers support is not assumed: failure is a hard stop, with no
fallback to an unverified flush or privileged volume operation. Native acceptance
must show that the supported OS/filesystem honors this protocol. A failure to write
stop evidence on an unavailable disk is not reported as a confirmed durable stop.
Cleanup/API-return observations cannot establish physical release or power-loss
durability. Existing session/intent evidence reduces uncertainty; it is not a new
rollback-resistant anchor.

`WindowsPersistentIdentityCustody` supplies held creation/open/public-SPKI source:
one explicitly named identity-generation key in CurrentUser Microsoft Software KSP,
P-256, Signing-only, export policy None, no machine/overwrite flags, requested
owner-only descriptor before finalization and strict metadata/descriptor readback.
Reopen requires exact name/context and matching public SPKI digest. No private
export, enumeration, deletion, certificate issuance, fallback provider or retry
exists. A partial named-key creation is preserved as uncertain, not called cleanup.
Provider descriptor persistence/readback, UI behavior and real custody are **NOT RUN**.

Relevant platform contracts:
[directory handles](https://learn.microsoft.com/en-us/windows/win32/fileio/obtaining-a-handle-to-a-directory),
[FlushFileBuffers](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-flushfilebuffers),
[CNG creation parameters](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.cngkeycreationparameters),
[key storage properties](https://learn.microsoft.com/en-us/windows/win32/seccng/key-storage-property-identifiers).
Conservative source checks may refuse a provider/filesystem until native evidence
supports it; they must not be weakened simply to obtain a passing native test.

### Genuine verification and remaining acceptance

- Initial targeted coordinator/journal run: 60 passed, no failed/skipped. Its file
  cases used the real transaction implementation and real synthetic temporary-file
  persistence. Test byte wrapping is deliberately NOT encryption, and its ordinary
  handle/directory callbacks do NOT validate the held Windows backend.
- Initial Windows denial/path run: 12 passed, no failed/skipped, before final path/
  ACL cases. It did not execute the blocked factories, DPAPI or CNG operations.
- Intermediate Release: 1,075 passed, no failed/skipped. Final negative absent-DACL
  coverage and the held coordinator factory then justified one final regression run.
- **Final Release: Restore PASS; Build PASS; 1,076 passed, 0 failed, 0 skipped;
  2/2 TRX files; exit 0.** This adds 44 unique cases over the previous 1,032 baseline.
  The journal/boundary classes contain 45 final cases, including the one existing
  boundary case, so those counts are not additional to the final suite total.
- **Gate B unchanged: 15 passed, 0 failed/skipped; discovery 8 -> 15**, sequential
  committed-source verifier, session `20261101T194559835Z`. The unrelated local
  verifier edit was not used, committed or changed. No full Gate B audit repeated.
- No new failing build/test runs occurred. Injected I/O exceptions were expected
  rejection-test inputs, not hidden test failures. Ordinary .NET SHA-256 runs for
  public binding/checkpoint calculations; no provider-free/crypto-free claim.
- Native production filesystem/ACL enforcement, DPAPI, persisted CNG, crash/power-loss,
  key deletion/cleanup, Android and networking integration remain **NOT RUN**.

Managed tests cover real-file clean reopen and tombstones, exclusive contention,
each transaction replacement phase, failed stop writes with retained session guard,
abandoned-session simulation, partial rollback, malformed/missing/oversized files,
unknown entries, failed close, pre-read clock failure, and coordinator disposal.
They do not simulate a real OS crash or validate native ACL/DPAPI/CNG behavior.
Windows policy tests use synthetic binary SIDs/ACL objects without account lookup
or permission changes. All original native Skips and approval/provenance/permit/
report holds remain unchanged; new native entry points also unconditionally deny.

Reproduce ordinary journal tests with:

```powershell
dotnet test tests/PeripheralBatteryCompanion.Tests/PeripheralBatteryCompanion.Tests.csproj --no-restore --configuration Release --filter 'FullyQualifiedName~PeripheralBatteryCompanion.Tests.DurableAuthorizationJournalTests|FullyQualifiedName~PeripheralBatteryCompanion.Tests.EnrollmentAuthorizationTests' --logger 'trx;LogFileName=gate-e-journal.trx' --results-directory test-results/gate-e-journal --verbosity minimal
./test.ps1 -Release
```

Latest full results are in `test-results/summary.txt` and `test-results/automated/`.
Earlier targeted results remain in `test-results/gate-e-journal/` and
`test-results/gate-e-native-denial/`. Four unrelated untracked files and the local
verifier correction remain excluded. No CI result is asserted.

Remaining protected acceptance must exercise the **actual held Windows composition**:
private-root/reparse/hard-link/DACL denial, supported directory flushing, DPAPI context
and wrong-user rejection, failure at every native write/flush/close, abrupt process
death/restart, exact persisted key creation/open and descriptor readback, and real
certificate association. Source revisions and exact operation/resource budgets need
fresh owner approval before any hold change or execution. Marker persistence can
itself fail; no claim that unavailable storage is magically safe. Full consistent
restoration of all old files remains the explicit E3.1 unsupported-rollback limit.

Production connectivity, live credentials and Stage E4 remain disabled. Gate E is
still BLOCKED and UNAUTHORIZED; this milestone implements the architecture without
claiming native validation or security acceptance.
