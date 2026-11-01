# Gate E native harness — read-only readiness review

**Review status: BLOCKED / NOT READY FOR NATIVE EXECUTION.** This is a static source review of the E3.2 branch. It is not an execution, security approval, permit, or gate transition. Gate B remains independently scoped and not authorized; Gate E remains BLOCKED.

## Review basis and provenance

- Repository: `Jau1Agiota/G-Hub-battery-tray-icon-and-smartphone-app-`
- Reviewed ref: `feature/stage-e3-2-pure-security-policy`
- Reviewed sources:
  - `tests/PeripheralBatteryCompanion.W1Offline.Tests/W1NativeLifecycle.cs` — blob `894c302ccc6620c7c0a32576a4b7ac2f9ec79746`
  - `tests/PeripheralBatteryCompanion.W1Offline.Tests/W1PreflightPlan.cs` — blob `905b1454bc3ff3ba677f891c2f03c64d8d266853`
  - `tests/PeripheralBatteryCompanion.W1Offline.Tests/W1Evidence.cs` — blob `29834160b4915da1f5db65fc4d3cdbb87ec96a79`
  - `tests/PeripheralBatteryCompanion.W1Offline.Tests/GateEAuthorizationGate.cs` — blob `7b872205dcefc9e76a2c28c495841ebc0f039ee9`
  - `tests/PeripheralBatteryCompanion.W1Offline.Tests/W1OfflineLifecycleTests.cs` — inspected earlier on this ref; native test has explicit Skip attribute.
- Review method: source inspection only. No build, restore, discovery, test execution, native calls, filesystem writes, permit access, or network/listener operations were performed as part of this review.

The separate Gate B offline test report documents execution on local branch `feature/stage-e2-snapshot-interop`, HEAD `ebca523c47aab663bff944d85b82881c131e2168`. Those local results are not evidence that the E3.2 branch sources reviewed here were built or executed.

## Findings

### BLOCKER-1 — Native approval guard is an unconditional throw

`GateEAuthorizationGate.RequireExplicitNativeApproval()` always throws `GateEExecutionBlockedException`. The source explicitly says native execution requires a separately reviewed and approved source revision. No approval verifier or approved revision binding is implemented.

**Effect:** `W1NativeLifecycle.Execute` cannot proceed beyond its first statement.

### BLOCKER-2 — Execution-provenance verifier is an unconditional throw

`GateEAuthorizationGate.RequireVerifiedExecutionProvenance()` always throws. Its comment identifies executable, loaded runtime, test assembly, provider, and source-revision provenance as unauthenticated.

**Effect:** Even if the first guard were changed, the second guard still blocks execution. The runtime version check inside `Execute` is explicitly defense in depth and does not establish exact loaded runtime, cryptography assembly, selected host, SDK, binary hashes, token, KSP, or provider.

### BLOCKER-3 — Permit acquisition is deliberately fail-closed

`GateEPermit.TryAcquireFromFile` parses and verifies a fixed-path permit candidate, but its final step explicitly refuses to proceed until a trusted nonce-ledger protocol is approved and implemented. It returns `false` unconditionally at the end. The source comments also identify unresolved path/ACL/ancestor-reparse and rollback/atomic-reservation concerns.

**Effect:** No caller can currently obtain a `GateEPermit` from this implementation. A valid signature and plan digest are not sufficient to produce a permit.

### BLOCKER-4 — Report destination verification is an unconditional throw

`W1NativeLifecycle.RequireVerifiedReportDestination()` always throws. Its comments identify unresolved ancestor reparse-point, directory ownership, ACL, and path-to-handle binding checks. The report stream is therefore never opened by the current code.

**Effect:** The native test's expected `ReportWriteReturned == true` cannot be satisfied by the current implementation even if earlier blockers were removed. The report path must not be made writable by simply deleting this guard.

### BLOCKER-5 — Native test's no-permit branch conflicts with the entry guard

The native test source calls `W1NativeLifecycle.Execute(null, null, now)` when permit acquisition fails, then expects a `NOT_RUN` result. But `Execute` calls the unconditional `RequireExplicitNativeApproval()` before creating/returning evidence or checking the plan/permit.

**Effect:** If the explicit Skip were removed without redesign, the no-permit branch throws instead of returning the asserted `NOT_RUN` evidence. The current skip must remain.

### BLOCKER-6 — Authorization unit tests do not establish permit authenticity or native authorization

`EnsureAuthorized` checks the environment variable `PBC_GATE_E_AUTHORIZED=1`, consumes the process-local one-shot latch, checks non-null plan/non-empty payload, validates digest length, and checks the time window. The test suite supplies strings such as `mock-permit` and `valid-permit-once`. This method does not itself verify the signed permit file, trusted nonce ledger, executable provenance, or native approval revision.

**Effect:** The four passing authorization tests establish only those narrow guard behaviors. They are not evidence that a valid owner permit can be acquired or that native execution is authorized.

## Native lifecycle and ownership map (static source description)

| Phase | Intended operation | Returned owner / cleanup | Current evidence limit |
| --- | --- | --- | --- |
| CreateKey | Create unnamed ECDSA P-256 CNG key using Microsoft Software KSP, no export policy, signing usage | `CngKey original`; later disposed once in finally | Not executed; provider behavior and effective custody unverified |
| Metadata | Read eight explicitly allowlisted properties | No new retained owner identified in source | Provider-returned allocation size is not hard-bounded before allocation; acceptance limit is post-call |
| WrapSigner | Construct `ECDsaCng` around existing key reference | `ECDsaCng signer`; disposed before original key | Exact ownership/disposal interaction with wrapped CNG key requires platform/runtime verification |
| CreateLeaf | Create self-signed leaf in memory, no store API shown | `X509Certificate2 leaf`; disposed first | Not executed; no store/persistence proof |
| InspectLeaf | Check public leaf fields, SPKI match, extension contents | Temporary public buffers | Accepted-output sum is a post-call bound, not a hard allocation bound; framework/native temporaries are excluded |
| DisposeLeaf | Dispose leaf | Attempted once if reference exists | A returned Dispose call is not proof of underlying provider resource deletion |
| DisposeSigner | Dispose signer | Attempted once if reference exists | Wrapped-key ownership semantics still require evidence |
| DisposeKey | Dispose original CNG key | Attempted once if reference exists | Does not prove key material/persistence absence or provider deletion |
| Report | Render redacted pre-write snapshot and write new file | `FileStream destination`, flush then dispose | Unreachable because destination verifier always throws; path security unresolved |

The nested `finally` structure attempts disposal in leaf → signer → original-key order, records whether each attempt returned, and does not claim that this proves resource deletion. Catastrophic process termination and failures outside handled exception classes have no cleanup/reporting guarantee. No deletion, reopen, enumeration, retry, or cleanup-success assertion should be added without a separately reviewed ownership protocol.

## Evidence required before any future native experiment can be considered

1. **Exact approved source and binary provenance:** reviewed commit/tree; hashes for test assembly, dependencies, host/runtime components, and the actual executable; exact SDK/runtime/architecture; proof the launched process loaded those exact artifacts; provider/KSP identity. A version-string check is insufficient.
2. **Independent owner approval:** explicit, scoped, one-shot approval bound to the exact plan digest, source revision, binary provenance, host, provider, time window, resource allowlist, and report destination. A process environment variable containing `1` is not sufficient authorization by itself.
3. **Permit/replay protocol:** a separately reviewed trusted nonce reservation/consumption protocol with atomicity, ACL/path/ancestor reparse protections, crash behavior, and rollback limits. Until then permit acquisition must continue to fail closed.
4. **Resource ownership proof:** documented ownership semantics for `CngKey`, `ECDsaCng`, certificate objects and provider handles, including partial factory failure and each disposal failure point. Distinguish Dispose-returned from actual provider resource/key deletion.
5. **Report destination proof:** dedicated new destination; verified directory ownership and ACLs; safe ancestor traversal/reparse handling; path-to-handle binding; create-new semantics; bounded output; privacy review; and explicit treatment of partial files. Do not weaken the current throw-only guard.
6. **Isolation and stop conditions:** exact machine/user/session, no production keys or stores, no listeners/network, bounded duration and resource scope, process termination/abrupt-failure plan, pre/post evidence that does not broaden into key-store scans or deletion.
7. **Test correction before unskip:** reconcile the no-permit branch with the unconditional entry guard and reconcile the expected report-write assertion with the currently blocked report verifier. Preserve the explicit Skip until a new source revision and exact execution scope are reviewed.
8. **Independent review of the resulting evidence:** distinguish intended behavior, observed provider responses, cleanup attempts, actual deletion/persistence evidence, and claims that remain unproven.

## Acceptance matrix

| Requirement | Classification | Basis |
| --- | --- | --- |
| Native approval guard | **BLOCKED** | Unconditional throw |
| Executable/runtime provenance | **BLOCKED** | Unconditional throw; no verifier |
| Signed permit parsing | **PARTIAL / NOT AUTHORIZING** | Parsing/signature path exists, but final nonce-ledger step always denies |
| One-shot permit/replay protection | **NOT VERIFIED** | Trusted nonce ledger/atomic reservation not implemented |
| Plan canonical digest | **PASS — narrow pure test only** | Frozen vector passed on local E2 branch; not native execution |
| Authorization guard unit behavior | **PASS — narrow unit scope only** | Four tests passed on local E2 branch using synthetic payload strings |
| CNG key creation and metadata | **NOT RUN** | Native lifecycle test skipped and entry guards block |
| Certificate creation/inspection | **NOT RUN** | Native lifecycle test skipped and entry guards block |
| Cleanup/resource deletion | **NOT VERIFIED** | No native execution; Dispose-returned is not deletion proof |
| Report path security/write | **BLOCKED** | Verifier unconditionally throws; path security unresolved |
| Production security/TLS/connectivity | **NOT VERIFIED / OUT OF SCOPE** | No platform proof or connectivity test authorized |

## Required decision

**Do not remove the native test Skip. Do not remove or bypass any fail-closed guard. Do not create or consume a permit. Do not execute CNG/certificate operations. Do not write a report.**

The current source is a deliberately blocked harness, not an executable experiment awaiting a simple approval token. The blockers above require reviewed implementation/design work and a new explicit owner decision. A future native experiment, Gate B authorization, and production connectivity are three separate scopes; none is approved by this review.

**Gate B: NOT_AUTHORIZED / independently scoped. Gate E: BLOCKED.**
