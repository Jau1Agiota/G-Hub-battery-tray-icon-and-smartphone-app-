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

The journal interface explicitly requires atomic, durable compare-generation
commits and independently verified restore/empty-state classification. There is
**no production journal implementation** in this PR: its fake journal drives
ordering tests only. Missing storage is never interpreted as new trust. Restart
after uncertain persistence requires protected consistency/recovery evidence;
constructing another coordinator is not legitimate recovery. No cross-process
rollback or stop-marker guarantee is claimed from the in-memory coordinator.

The existing DNS-oriented `LanApprovalSession`, in-memory phone registry, snapshot
request core, monitoring, UI and startup remain unchanged. The new IPv4 identity
must not be combined with the old DNS profile to claim production eligibility.
There are no production call sites for the new coordinator or native adapter.

## Permitted verification actually performed

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
- [ ] Durable journal: restrictive ACL/ancestor/reparse/path-to-handle validation,
  create-new/atomic replacement/flush evidence, generation/tombstone consistency,
  crash interruption and restart safety. Cross-process uncertain-stop persistence
  and full consistent rollback remain unsolved; no plaintext fallback permitted.
- [ ] Authenticated exact-operation native permits, trusted atomic one-time nonce
  consumption, executable/runtime provenance and verified report destinations.
  Existing W1 acquisition/consumption still always deny.
- [ ] Persisted nonexportable CurrentUser Software-CNG P-256 identity, restricted
  ACL and verified certificate/TLS association. Existing **ephemeral** W1 source
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
