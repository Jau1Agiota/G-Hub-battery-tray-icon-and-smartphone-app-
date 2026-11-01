# Gate E — independent native harness readiness audit

**Overall verdict: FAIL for execution readiness. PASS for retaining the current execution hold. Gate E remains BLOCKED.** Gate B remains independently scoped and unapproved.

This document records the independent review returned for the native W1 harness. It supplements, and does not replace, `docs/gate-e-native-harness-readiness-review.md`.

## Review provenance and limits

The independent reviewer reports that it inspected the named source files and isolation documents at Windows commit `38ee131cea7d326c927086a195bbd0f4273f6e8d`, and that the worktree was clean.

The reviewer reports performing source/document reads and read-only Git inspection only. It reports no .NET commands, discovery, tests, native operations, permit/ledger/credential access, network-interface inspection or listeners.

These are reviewer-reported provenance details, not independently reproduced in this document. The previously recorded Gate B execution results came from local branch `feature/stage-e2-snapshot-interop`, HEAD `ebca523c47aab663bff944d85b82881c131e2168`; they must not be represented as execution of commit `38ee131...` or of the E3.2 review branch.

## Independent verdict

- Source-level denial holds and limited-evidence labeling: **PASS**.
- Safe isolated execution readiness: **FAIL**.
- Native security properties: **NOT VERIFIED**.
- Gate B: **independently scoped; NOT AUTHORIZED**.
- Gate E: **BLOCKED**.

## Findings corroborating the prior readiness review

The independent review confirms the six previously recorded blockers:

1. The native test is explicitly skipped.
2. `RequireExplicitNativeApproval()` always throws.
3. `RequireVerifiedExecutionProvenance()` always throws.
4. Permit acquisition ends by returning false pending a trusted nonce-ledger protocol.
5. Report-destination verification always throws.
6. The native test's fallback and expected report-write assertion cannot succeed under the current guards.

These are separate fail-closed holds. They are not completed authorization mechanisms and must not be bypassed.

## Additional independent findings

### 1. Test-runner isolation is incomplete

The project disables default items but does not explicitly compile `AssemblyInfo.cs`, which contains the nonparallel assembly attribute. The project also lacks explicit output-copy configuration for `xunit.runner.json`.

The presence of these files in the repository does not establish that the test assembly receives the nonparallel attribute or that the runner loads the intended configuration. Effective runner behavior must be verified from the exact compiled assembly and testhost configuration before any future execution approval.

### 2. Secondary authorization does not validate the permit

`EnsureAuthorized` checks the environment flag `PBC_GATE_E_AUTHORIZED=1`, a process-local latch, nonempty payload, digest length and time window. It does not validate the supplied permit payload. The caller passes `permit.ToString()`, not a verified authorization representation.

The unconditional native holds currently prevent this from enabling execution. Nevertheless, the method is not a cryptographic authorization boundary.

### 3. One-shot authorization is unfinished

Permit acquisition deliberately refuses issuance until a trusted nonce-ledger protocol is implemented. Process-local latches cannot prevent reuse in another process. A reset helper exists and is called by companion test setup/teardown, so its call graph and effects must be included in the independent build/discovery review.

No permit replay guarantee should be claimed from the process-local latch.

### 4. Authorization preflight has native effects

The permit acquisition helper uses native file access and imports/verifies an ECDSA public key before the secondary authorization check. This is not a purely managed preflight. The API and resource scope of these operations requires explicit review and authorization.

### 5. Time and clock assurance are incomplete

The test captures `now` once and reuses it. Clock confidence is hardcoded rather than established from an independently accepted basis. The validity window is the first ten minutes of the current hour, not ten minutes from invocation. No fresh deadline check occurs immediately before key creation or signing, and no bounded native-call duration is established.

### 6. Dependency and historical-document evidence is incomplete

The inspected isolated project directory has no resolved dependency lock. Exact SDK/runtime/testhost/adapter/provider and loaded-binary provenance remains unverified. Some historical documents reportedly describe the project as nonexistent or inputs as unset; these must be reconciled against current source before an execution approval packet is considered complete.

## Intended native sequence and ownership summary

If a future revision is separately reviewed and authorized, the source's intended sequence is:

1. Validate exact owner approval, source/binary/runtime provenance, execution context, plan and one-shot authorization.
2. Validate the synthetic plan inputs and output bounds.
3. Check Windows/x64/interactive context and runtime version.
4. Mark native entry before invoking the key factory, because factory failure may still have acquired internal resources.
5. Request one unnamed P-256 signing key from Microsoft Software KSP with export policy `None`, no machine-key flags, no parent window and null UI policy.
6. Observe eight allowlisted metadata fields. Missing required metadata, malformed values, errors or mismatches stop progression. Only key-name absence/empty name is accepted.
7. Wrap the existing key in `ECDsaCng`; do not generate another private key.
8. Create one signed public-only, non-CA certificate using the explicit generator/Create overload. No certificate-store insertion or private-key association is intended.
9. Inspect public fields, IP SAN, EKU, key usage, constraints, validity, serial and matching SPKI values.
10. Attempt disposal in order: **leaf → signer → original key**.
11. Render a bounded, redacted report and, only after separately verified destination authorization, create a new report file, write, flush and close it.
12. Stop for review. The intended outcome remains `LIMITED_OBSERVATIONS_CLEANUP_UNCONFIRMED`, not a security pass.

### Resource ownership and evidence limits

| Resource | Owner / release attempt | Evidence limit |
|---|---|---|
| Underlying unnamed private key/provider references | Returned original `CngKey`; last disposal attempt | Dispose returning does not establish native release or nonpersistence |
| Existing-key signer reference | `ECDsaCng`; disposed after leaf and before original | Actual servicing ownership behavior remains unverified |
| Public certificate context | Returned `X509Certificate2`; first disposal attempt | If creation fails before returning, this code has no owner to dispose |
| Request, generator, extensions and helper objects | Local managed references | Hidden/transient allocations require runtime correspondence review |
| Public DER/SPKI/hash/ASN.1 buffers | Local managed arrays | Bounds are checked after allocation, not hard allocation caps |
| Evidence object | Calling test | Redacted observations, not a recovery journal or cleanup receipt |
| Report stream/file | Local `FileStream`; close attempt after write/flush | Partial files are preserved; no deletion, overwrite or retry |
| Permit file handle and ECDSA verifier | Authorization helper; `using` disposal | Additional resources outside the core key/leaf budget; import/native effects require separate approval |

No named-key deletion, enumeration, reopen, store cleanup or speculative recovery is provided or authorized.

## Failure-path expectations

| Failure point | Expected source behavior / limit |
|---|---|
| Skip or unconditional approval/provenance hold | No experiment key or certificate creation; currently stops before lifecycle evidence is returned |
| Permit parsing, hashing or signature verification | Stop before experiment creation; helper owners use `using`; exceptions may escape outside lifecycle evidence handling |
| Invalid plan, consumed one-shot state or unsupported runtime/context | No experiment creation; some preflight operations can throw rather than return `NOT_RUN` |
| Key factory throws before returning | `NativeEntered=true`; no returned key to dispose; internal acquisition/release uncertain |
| Metadata observation fails | Stop; dispose original key; signer/leaf cleanup is not attempted |
| Signer construction throws | Dispose original key; partial wrapper ownership unverified |
| Request/extension/serial preparation fails | Dispose returned signer, then original key |
| Generator/signing/certificate loading fails | Dispose returned owners; unreturned certificate context release is unverified |
| Public certificate inspection/bound check fails | Dispose leaf, signer and original; record `STOP_INCONCLUSIVE` |
| Leaf disposal throws an ordinary exception | Record failure; nested `finally` still attempts signer and original disposal |
| Signer disposal throws an ordinary exception | Record failure; original disposal is still attempted |
| Original disposal throws | Record cleanup uncertainty; no deletion or retry |
| Report rendering/destination validation fails | Native-owner cleanup already attempted; report stream is not opened when destination hold throws |
| Future authorized file creation/write/flush failure | Preserve partial file; attempt stream disposal; record report uncertainty |
| Stream disposal fails | Preserve earlier failure, record secondary failure, report completion remains false |
| OOM, stack overflow, abrupt death, native hang or unexpected UI | No reliable reporting/cleanup guarantee; no watchdog, kill, UI dismissal or retry is authorized |

The first failure is preserved; later failures are secondary observations. This is a source-level evidence-discipline pass, not proof of provider cleanup.

## Claims the experiment cannot establish

| Claim | Available observation | Classification |
|---|---|---|
| Non-exportability | Requests `ExportPolicy.None`; expects matching metadata | **NOT VERIFIED**; metadata does not prove private-export denial |
| Unnamed/ephemeral characteristics | Null key name; expected CLR ephemeral marker; absent/empty name | **NOT VERIFIED**; does not establish absence of persisted provider state or crash residue |
| Current-user protection | No machine-key request; expected scope metadata | **NOT VERIFIED**; ACL protection/effective-access denial not tested |
| Public-only certificate | Expects `HasPrivateKey=false`, matching SPKI and precise extensions | **NOT VERIFIED at runtime** |
| Correct certificate signature | Certificate created through signer; signature OID checked | **NOT VERIFIED independently**; resulting signature is not independently verified |
| Native cleanup | Dispose attempts and return values recorded | **NOT VERIFIED**; managed return is not an OS-level release receipt |
| TLS identity/trust | No TLS or chain validation | **NOT VERIFIED / OUT OF SCOPE**; public-only leaf is not an associated TLS identity |

The independent review notes that a later Gate P resolution document records the historical VMR/runtime source-revision discrepancy as resolved through official manifest correspondence. That closes the historical mapping question only; it does not establish which binaries a future testhost actually loads.

## Conditions that must remain blocking

Before any future native experiment can be considered, independently reviewed evidence must establish:

1. Exact Gate B approval for required resolution/build/discovery scope.
2. Effective single-case, nonparallel, no-retry runner isolation, including companion-test side effects.
3. Exact source, binary, runtime, adapter and provider provenance.
4. Trusted, replay-resistant one-shot authorization.
5. Explicit owner-approved inputs, real clock-confidence basis and execution-window handling.
6. Authorization for every preflight/native API and resource, including public-key verification helpers.
7. Verified report destination and complete output/privacy handling, including testhost exceptions.
8. Accepted limitations for post-allocation bounds, possible UI/hangs and `CleanupUnconfirmed`.
9. A separately approved response to partial acquisition, disposal uncertainty or unexpected state.

Unexpected UI, extra keys/imports, named/store state, private-export paths, ambiguous ownership, missing metadata, provenance mismatch or cleanup uncertainty must cause **STOP**, with no automatic rerun or speculative deletion.

## Acceptance matrix

| Requirement | Independent classification |
|---|---|
| Source-level denial holds and limited-evidence labeling | **PASS** |
| Safe isolated execution readiness | **FAIL** |
| Native approval guard | **BLOCKED** |
| Executable/runtime provenance | **BLOCKED** |
| Signed permit parsing | **PARTIAL / NOT AUTHORIZING** |
| One-shot permit/replay protection | **NOT VERIFIED** |
| Canonical digest | **PASS — narrow pure test only**, on the separately recorded local E2 branch |
| Authorization unit behavior | **PASS — narrow unit scope only**, synthetic payload strings on the separately recorded local E2 branch |
| CNG key creation and metadata | **NOT RUN** |
| Certificate creation/inspection | **NOT RUN** |
| Cleanup/resource deletion | **NOT VERIFIED** |
| Report path security/write | **BLOCKED** |
| Production security/TLS/connectivity | **NOT VERIFIED / OUT OF SCOPE** |
| Gate B | **NOT AUTHORIZED / independently scoped** |
| Gate E | **BLOCKED** |

## Required decision

**Keep the native test skipped. Keep every fail-closed guard. Do not create or consume a permit. Do not execute CNG/certificate operations. Do not write a report.**

This harness is not an experiment that merely awaits an approval token. It needs reviewed design/implementation work, exact runner and binary provenance, and a new explicit owner decision. A native experiment, Gate B authorization, and production connectivity remain separate approval scopes.

No authorization was changed by this review. No merge, release or gate transition is authorized.
