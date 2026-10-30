# A2.2C6 pre-binding safety hardening

Decision: retain fail-closed orphan handling. Native execution remains **NO-GO pending owner review**. This change constrains cleanup authority; it does not eliminate orphan risk or prove nonexportability, policy enforcement, production readiness, or absence of provider UI.

Baseline: 33b421b4f28a7ccfd4ca1afdfc63288551335a33. Existing branch and draft PR #13/base are retained. Owner-reported Windows SDK 10.0.402 Release restore/build passed with 921 tests, zero failed/skipped. That is historical owner evidence, not execution of this change. Baseline GitHub check-runs reported two completed successful windows checks.

## Lifecycle and deletion decision

| Boundary | Explicit proof state | Permitted behavior on failure |
|---|---|---|
| Preflight/journal before create | NotStarted | No adoption, reopen or deletion |
| Named create entered, including error after side effects | CreatePending | Unknown; preserve possible orphan |
| Create returns owned handle; SET operations | CreatedUnbound / ConfiguredUnbound | Unknown; release handle only |
| Finalize entered/returned | FinalizePending / FinalizedUnbound | Unknown; no extra finalize or cleanup retry |
| Public export or Bound append in progress | BindingPending | Unknown even if side effect/flush happened |
| Bound append returned | BoundUnverified | Provisional record alone never authorizes cleanup |
| Journal bytes/ACL/plan plus live provider/name/user scope/SPKI verified | BoundVerified | Cleanup eligible only with owned handle and fresh checks |
| Original handle release pending or completed | CreationReleasePending / CreationReleased | No cleanup through stale handle; ambiguous release stops |
| Exact reopen pending/returned | ReopenPending / ReopenedUnverified | No cleanup until independent live identity verifies |
| Reopened identity verified | ReopenedVerified | Metadata/sign/export error can retain authority; fresh cleanup verification still mandatory |
| DeleteAttempted append/native delete pending | DeletePending | No retry; ambiguous result becomes Unknown |
| Native success and consumed handle | Deleted | Deleted + Completed records required for confirmed cleanup |
| Delete succeeded but evidence failed | DeletedEvidenceUncertain | No second delete; evidence remains unconfirmed |

The state machine starts anew for each invocation. Unknown cannot be promoted by later journal contents or matching names. Reopen is reached only after durable binding, independent live identity verification, and confirmed creation-handle release in the current flow. No restart recovery, adoption or broad enumeration exists.

Safe deletion requires current-operation fresh creation, valid canonical public SPKI, successful durable Bound append, independent journal and exact live identity verification, an owned live handle, no intervening journal/identity uncertainty, fresh verification immediately before cleanup, and a flushed DeleteAttempted record. Native status zero AND both returned/actual consumed-handle states are mandatory. Journal completion must then succeed to report confirmed cleanup.

NCryptFreeObject releases a handle; it does not prove deletion of a named persisted key. NCryptDeleteKey success consumes the handle: it must not subsequently be freed. Native status failure leaves an owned handle for one release; unknown native disposition is abandoned rather than risking double free. The existing native handle ledger is unchanged. Cleanup authority and native handle disposition remain separate.

## Alternatives considered

1. Eliminate the window through reordered transaction, ephemeral promotion/import or new evidence model: no demonstrated atomic CNG/journal transaction exists here. Introducing new native behavior or weaker proof would require separate approval; not implemented.
2. Early deletion through the still-live created handle: handle possession, operation ID, key name and provisional journal do not independently establish the required exact persistent identity. No early-delete API path was added.
3. Preserve possible orphan without mutation: implemented. Closing a returned handle may leave a named key. An error after creation can leave a key even without a returned usable handle.

## Threat and failure model

- Name collision or concurrent same-ID operations: preflight plus fresh exclusive journal reject reuse; no overwrite flag, adoption or collision deletion. A racing native create failure is uncertain and never authorizes cleanup. Different IDs have independent journals.
- Partial journal write, ACL/reparse failure or failed flush: invalidate authority, including exceptions after a possible successful write. A visible Bound record is insufficient.
- Crash/power loss: synthetic before/after-flush interruptions exercise the core's fail-closed decisions, not actual OS durability. Abrupt death may bypass finally; restart never recovers/deletes. Windows ACL and durable-storage behavior remain unverified here.
- CNG error after side effects, unknown/invalid returned handle, finalize or public-export failure: preserve possible orphan, no retry/reopen/recovery. Native adapter semantics are not established by fake tests.
- Unexpected provider UI or cancellation: STOP and await owner review; no promise of no dialog, bounded cancellation or timeout. Flags remain zero. No new watcher, kill, silent flag or native policy was introduced.
- Identity mismatch or journal tamper after binding: invalidate ownership authority. Known metadata/sign/export errors may retain verified authority only for fresh exact cleanup checks.
- Delete failure/unknown consumption: one attempt, no retry, never infer absence. Successful delete followed by record failure remains evidence-uncertain.
- Same-user hostile process/admin interference and universal power-loss atomicity are outside the guarantee. Provider/name/SPKI checks are not key-ACL proof.

Owner action on an orphan/uncertain result: stop, preserve evidence and request a separately reviewed investigation. This stage authorizes no enumeration, cleanup command, prior-ID inspection or native execution.

## Preserved boundaries and verification

No native adapter, journal security/durability implementation, native handle ledger, full-capacity private-buffer clearing, original A/B fixture or production application code changed. Existing tests and assertions remain. The new ownership enum is redacted categorical output only.

The preview-only ID ff28360e68f249439294475e8da21614 is explicitly rejected by the hardened permit validator, case-insensitively. No new operation ID or executable command is provided. All three retired IDs remain rejected.

Portable tests include every normal lifecycle boundary, error transitions, unknown handles, possible creation side effects, provisional binding, and terminal deleted-evidence uncertainty; existing collision, replay, before/after-flush, identity, zeroization and delete-failure tests remain. See accompanying report for actual commands/counts. No real CNG, key/certificate operation, native fixture, preview, Windows journal creation, physical device test or native Windows experiment ran.

Executed verification (.NET SDK 10.0.403 on Linux):
- dotnet restore PeripheralBatteryCompanion.sln --locked-mode: PASS.
- dotnet build PeripheralBatteryCompanion.sln -c Release --no-restore: PASS, zero warnings/errors.
- dotnet test tests/PeripheralBatteryCompanion.Tests/PeripheralBatteryCompanion.Tests.csproj -c Release --no-build --no-restore --logger trx: 889 passed, zero failed/skipped (15 added to the 874 portable baseline).
- git diff --check: PASS.

Windows-only test execution and native ACL/durability/UI/CNG proof: NOT RUN. Building Windows targets on Linux is compilation only.
