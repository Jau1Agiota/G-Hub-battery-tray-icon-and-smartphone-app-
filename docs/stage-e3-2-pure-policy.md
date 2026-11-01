# Stage E3.2 — isolated deterministic security policies

Status: test-only behavioral models, not production security. Gate E BLOCKED;
Gate B independently scoped. No commit/push/merge/release performed.

## Approval and contract

The owner conditionally approved E3.1 and explicitly authorized E3.2 pure policies
once five points were finalized. The identical docs/stage-e3-1-security-contract.md
in both repositories records those points in section 12: fail-closed 64-instance
exhaustion, immediate revocation/strict expiry, transcript/endpoint binding,
uncertain-persistence/rollback behavior and unchanged Gate B/E restrictions.
Historical architecture remains proposed except for that scoped authorization.

## Implementation boundary

Android app/src/test/java/dev/peripheral/battery/securitypolicy/ contains:
- PureSecurityPolicy.kt: explicitly supplied clock/commit/context models for
  authorization, revocation, replay and sequential lifecycle/evidence decisions.
- PureSecurityPolicyTest.kt: expiry/release boundaries, pending revocation,
  substitution, replay/reconnect, 64/65 instances, failure latches, cancellation,
  deadline, malformed/synthetic data and defensive-copy regressions.
- EvidencePreservationTest.kt: all 14 exact Windows-emitted accepted E2 publications,
  preserving every parsed field across duplicate reads and connection loss.

Models compile only in JVM tests; nothing is added to main/debug/release runtime,
manifest, startup, dependency configuration or Windows executable sources. Existing
mocksession and simulation behaviors remain unchanged. No credentials, certificates,
pairing protocol, TLS implementation, native store, cryptographic proof, sockets,
provider refresh or real-clock access is implemented. Binding values and nonce labels
are deliberately fictional strings; a supplied peerVerified Boolean is a test
observation only. Commit values are synthetic outcomes, never disk operations.

Authorization has a two-event revocation model: request immediately denies traffic;
completion acknowledges only COMMITTED. Failed/uncertain completion stays disabled.
Authorization is rechecked at modeled release/receipt, including duplicates. At
expiry there is no grace. Wall or monotonic regression/uncertainty latches denial.

Replay is scoped to one fixed injected trust/authorization context. History holds
64 instances maximum; the 65th latches recovery without mutation/eviction. Missing
state, known rollback and uncertain/not-committed state latch recovery; there is
no reset/recovery API. Duplicate COMMITTED means existing stored state is known
consistent, not a demand to write new measurement data. Duplicates update channel
time only, never evidence/progression time. Full parsed immutable wire evidence
is retained; displayed evidence is hidden after loss/error/revocation.

These models have no durable restore, secret generation, cryptographic randomness,
actual nonce/digest/TLS verification or multi-threaded networking guarantees. They
accept explicitly injected authenticated-context observations as fiction. A new
model object cannot be presented as legitimate recovery under existing trust.
Nonce-label history is in-memory/unbounded for tests, not a deployment resource
policy. Future transport must enforce its independent bounds and real protocol.

## Verification

Only existing authorized Android offline JVM tests are used:
`source /workspace/.tools/android-env.sh`
`./gradlew --offline --no-daemon --max-workers=4 testDebugUnitTest`

Gradle's local build IPC requires the managed execution allowance; this is not an
application/LAN listener. The first default-sandbox attempt failed before tests with
SocketException: Operation not permitted. The first executed suite found a test
mutation expecting whitespace absent from compact Windows JSON; mutation and its
precondition were fixed. Final results are recorded below after verification.

No Windows tests/build/discovery, native storage/TLS/credential experiment, APK
build, device test or connectivity check is run for this test-source/document-only
change. E2 owner-reported Windows results remain the serialization baseline.

## Remaining risks and next gate

No platform-security claims: CNG/DPAPI/Keystore custody, crash persistence, hostile
full-state rollback, same-user/admin compromise, stolen bearer, active QR race and
real TLS/IP-SAN validation remain unproven. Constant injected observations do not
solve these. The exact full-transcript comparison is a security requirement for
later enrollment, not implemented pairing.

Next: owner review of this diff/test evidence, then separately scoped platform-proof
preparation. Any new sensitive harness needs applicable Gate B source/import/build/
discovery review. Any native execution needs its own exact authorized scope and
provenance; the existing offline W1 scope does not authorize TLS, DPAPI, Keystore,
sockets or credential operations. No automatic E3.3 implementation/execution, native
retry, credential issuance, exposure, gate transition, merge or release follows.

## Historical measured results (pre-lifecycle-correction)

Android offline JVM suite: **268 passed, 0 failed, 0 errors, 0 skipped**, including
31 E3 policy tests + 14 exact-publication evidence cases (45 new), plus 223 existing.
Command completed BUILD SUCCESSFUL. No skipped native checks are counted as passes.
Both canonical manifests: 20 SHA256 entries each verified. Android E2 manifest:
27 payload hashes verified, all 29 resource files (including manifest and hash list)
unchanged from HEAD.
Production, permissions, build configuration, gate guards and existing tracked files
unchanged. Diff/whitespace and file allowlist checks passed. Both contract copies
are byte-identical. No Windows executable test/build/discovery run.

## Final Android lifecycle-correction verification

The later Android-only Kotlin test-model correction makes receive use checkDeadline
for the same transient DISCONNECTED outcome at the 30-second boundary, regardless
of whether the timer or the response event arrives first. It preserves replay
history/publisher bindings and checks authorization and clock failures first.
Five additional regression tests cover both event orders, checked reconnection,
retired publishers, replay regression/modified duplicates, identity substitution,
and expiry coincident with the deadline. Targeted tests reproduced the receive-first
failure before the correction. No Windows implementation changed.

The subsequent complete offline Android JVM suite passed **273 tests, 0 failures,
0 errors, 0 skipped**: 36 policy tests + 14 evidence cases = 50 E3 cases, plus
223 existing tests. Command: ./gradlew --offline --no-daemon --max-workers=4
testDebugUnitTest. BUILD SUCCESSFUL. This result was obtained on Android's JVM
test project; it is not independent Windows execution. The earlier 268-test result
above is retained as pre-correction history, not the final lifecycle result.

Both finalized E3.1 contracts remain byte-identical. All 29 E2 resources and both
20-entry canonical hash sets were verified unchanged after correction. Production,
manifest, permissions, dependencies and security guards were unchanged. No Windows
build/test discovery, native crypto/storage/TLS operation or connectivity experiment
was run. The scoped closure/publication task changes this Windows report only and
does not rerun tests; it uses the recorded Android results and independent review.

The lifecycle closure blocker is resolved. Publication still requires verified
branch ancestry, exact file scope and draft-only PRs; no merge, release or security
gate transition is authorized. Gate B is not approved and remains independently
scoped. Gate E remains BLOCKED. Platform custody, durable storage, full rollback
resistance and production TLS/connectivity remain unvalidated.
