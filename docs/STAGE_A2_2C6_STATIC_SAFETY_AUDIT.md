# A2.2C6 — focused static safety audit / code-only correction

**Recommendation: GO for OWNER Windows regression and pure read-only preview ONLY.
Native execution and persisted policy proof remain NO-GO. No command supplied.**

## Scope / baseline

Expected/reviewed HEAD `522909e08bfd57b03be927ec4554ec5c6f9315e8`; exact feature branch
`feature/stage-a2-2c5-isolated-security-harness`, clean tracked tree initially. PR #13
draft on unchanged #12 base. Read AGENTS.md, actual engine/model/native/journal code,
source/test wiring, implementation review, prior proof design and Microsoft references.
Only Option2 code/tests/docs changed. A/B1, original fixture, production tray/Android/
security/pairing/TLS/DPAPI/firewall and prior journals/resources remain untouched.

GO is limited to an owner-controlled checkout/build/normal non-experiment regression
and pure preview after ordinary verification. Normal builds/tests may write their
usual outputs; "read-only" here means no key/journal/security-fixture mutation. It
is not approval of the native execution mode or an experiment ID. Codex ran neither
owner Windows actions nor preview/native entrypoint, and generated no fresh ID.

## Confirmed corrections / baseline file:line evidence

**F1 — medium defense-in-depth, private clearing order.** Baseline Option2Engine.cs:
122–124 could allocate/add an output-observed evidence record before ZeroMemory.
A reporting/allocation exception there would enter outer cleanup with uncleared output.
The previous report excluded fatal memory exhaustion; this nevertheless left avoidable
fallible work ahead of clearing. No real leak/Windows failure is asserted.

New Option2Safety.cs:8–16 scans and clears in finally, returns a value-only observation,
with no callback/allocation/reporting/journal access before clearing. Within the buffer-finalization block, engine obtains
that result and updates memory state before fallible evidence work. Full owned span
cleared, including failed-fill/invalid-length capacity. Synthetic reporting exceptions
after the actual production helper verify all bytes already zero. Catastrophic memory
failure/kill/native corruption are still not claimed recoverable.

**F2 — low/medium ownership precision, overbroad native-delete catch.** Baseline
WindowsOption2Api.cs:37–47 caught not only the native call but also result creation
and known-consumption bookkeeping. A later managed exception could reset already
known ConsumedByDelete into OwnershipUnknown. The SafeHandle had already been
invalidated, so this was conservative/inaccurate, not demonstrated unsafe deletion.

New production Option2KeyOwnership ledger (Option2Safety.cs:31–47) catches only the
native callback. Confirmed code0 consumption is preserved before invalidation/result
creation; known nonzero remains owned, unknown call outcome abandons ownership. A
one-shot release guard prevents re-submission after a throwing release. Windows Key
uses this ledger under its existing lock and passes its actual Native.DeleteKey and
SetHandleAsInvalid callbacks. Tests exercise that same ledger with managed callbacks:
success/no-free, failure/one-free, unknown/no-free, no second delete, throwing release,
and downstream report failure after success. No accepted status or proof criterion
was broadened; no native callback ran in tests.

**F3 — disclosure improvement, expected pre-binding orphan risk.** Ordinary failures
in SET0/usage/UI, finalize, public export or binding/flush can leave a named key before
durable SPKI binding, not only on crashes. New pure preview wording says this explicitly,
including that handle release is not persistent deletion. No preview entrypoint ran.

## Pre-binding orphan decision (high operational risk before any native approval)

Retain current no-delete-until-durable-binding rule. A returned creation handle and
planned name provide some provenance, but authorizing another deletion path would
change the approved ownership contract. NCryptDeleteKey is documented for an opened
key; unfinalized/partially configured key semantics are not established here. Do not
finalize/reconfigure a failed key simply to obtain binding, reopen by prefix/name for
recovery, assume absence after FreeObject, or delete an old journal's planned identity.

Before binding, dispose only known owned handles and report orphan/unconfirmed. No
automatic additional cleanup is justified under this task. This can leave native
persistent resources on ordinary failure; owner must review that risk before ANY
native approval. Later recovery needs separately reviewed exact scope; none designed
as an automatic action or command here. Read-only verification creates no such key.

## Lifecycle / ownership audit

Actual ordered gates: exact consent/context/provider -> definitive absent planned key
-> fresh exclusive Planned journal -> one named key -> separate SET0/2/UI evidence ->
one finalize -> public binding/flush/live journal/identity -> release creation handle
-> exact reopen -> mandatory persisted readback0/all metadata -> signature possession
-> journal gate -> at most one fixed size/fill -> buffer clear -> exact owned cleanup.
No export before all prerequisites; no retry/provider/blob/flags fallback.

Delete consumes handle on success; SetHandleAsInvalid prevents subsequent FreeObject.
Known failure permits one release of still-owned handle, never another delete. Unknown
native call outcome invalidates/abandons managed ownership, leaves explicit uncertainty
rather than a possible double-free. Ledger State Released means release attempted/
managed disposition, NOT native success: engine separately checks ReleaseSucceeded.
Generic handle release is never exact persistent-key cleanup evidence.

SafeHandle P/Invoke references protect handles during calls. Synchronous engine retains
provider until key processing/release; key delete/dispose lock serializes those actions.
No general concurrent-use lease or thread-safe API is claimed. Partial acquisition,
success/invalid output and failed free follow unchanged shared checked helpers.
If release is deferred/unconfirmed/throws, no successful final result is inferred.

Exact cleanup requires live plan/journal/SPKI/name/provider/user scope and a usable
owned handle; failed metadata with known public ownership can still clean that exact
owned key, but identity/journal uncertainty blocks deletion. Missing/reopen failure
cannot trigger a recovery open. Known delete/record/release failures yield FAIL or
cleanup unconfirmed. Existing tests retain unrelated-identity refusal/no unsafe delete.
Completed journal stage describes key deletion/public record completion before final
resource closes, not independent disk absence or proof the journal closed itself.
Final provider/journal-close failure downgrades final cleanup; read final evidence,
not a standalone Completed/free0 token. No overall policy PASS path exists.

## Journal / path / durability audit

Fresh operation directory uses atomic CreateDirectoryW with owner/protected DACL;
preexisting operation path refuses even if owner matches. Existing namespace only
accepted with exact owner-only ACL; no repair. FileMode.CreateNew/FileShare.None/
WriteThrough/Flush(true), local fixed drive, bounded65536 public bytes, strict stage
protocol, exact in-memory expected-content/plan/SPKI and owner/reparse rechecks gate
export/deletion. Failed flush/binding/content/ACL marks unsafe, never adopts/restores.
Journal contains public identifiers/binding only; no private bytes/SID/path in reports.

**Residual native gate:** checks are path-based, not a general handle-relative defense
against hostile same-user/admin TOCTOU or directory substitution. Exclusive sharing
and owner ACL narrow ordinary interference but do not defeat the owner/privileged
attacker; journal is not anti-rollback/tamper proof. No reparse-proof NT path traversal
claim. Stronger threat model requires separate native design/audit, not relaxed checks.
Append + Flush(true) is not guaranteed atomic multi-sector/power-loss storage. No
process-restart adoption or recovery scan exists; incomplete stages remain reviewable
orphans. Real Windows ACL/inheritance/sharing/reparse/flush behavior NOT RUN. These
limits do not block pure preview/regression, but block native/production readiness.

## Native ABI / encoding / UI review

Compared official NCryptOpenKey/CreatePersistedKey/SetProperty/GetProperty/FinalizeKey/
SignHash/ExportKey/DeleteKey/FreeObject contracts with actual declarations. Winapi
default calling convention, explicit Unicode entrypoints for strings, native-width
SafeHandles/pointers,32-bit DWORD lengths/flags and SECURITY_STATUS match. NULL key
wrapping/params/padding where required; flags0 unchanged. Partial out handles are not
adopted on error. Only NTE_BAD_KEYSET with invalid output is definitive absent-key
preflight; generic unsupported/NOT_FOUND/access error never means safe absence.

ECCPUBLICBLOB72 = header8 + public X32/Y32, ECS1. Canonical P256 SPKI91 uses correct
OID/public point layout. ECCPRIVATEBLOB104 = header8 + X32/Y32/d32, ECS2/cbKey32.
Payload integers are big-endian; header DWORDs little-endian. NCrypt ECDSA signature
uses fixed-width r||s and verifier explicitly requests IEEE-P1363/64 bytes for P256.
Actual Windows marshalling/provider/signature behavior remains NOT RUN; pure patterns
are not mathematical/native key proof. Public-only verifier imports no private scalar.

UI Policy is appropriate to this persisted object: version1/DWORD flags0 plus three
native pointers,20 bytes x86/32 x64/ARM64; full versioned structure required. It is
set before finalize/read after reopen. Flags0/NULL strings do not guarantee no UI.
Unexpected dialog/hang requires STOP without interaction; no silence flags, timer,
watchdog, kill, UI automation or native run added. B1 no-dialog observation cannot
prove this target's future behavior. UI/native compatibility remains a separate gate.

## Secret hygiene / outcomes / consent

Entire allocation is cleared by the non-allocating helper before cleanup reporting,
journal flush or delete. Earlier in-try phase evidence is still protected by finally. Fill success OR observed partial output forces FAIL, even if native error
also returned. No private copy/resize/serialization/PFX/log. Challenge/hash cleared;
public binding/signature copies are not private export. No claim for provider/runtime
internal buffers, paging/dumps, native corruption, process termination or absolute erasure.
Unsupported/type/state/access export rejection is observed INCONCLUSIVE, not causal
policy enforcement; actual readback0/signature/control success do not change that.

Exact version/confirmation/UI-orphan acknowledgement, ordinary owner, v4/variant,
all three retired IDs and per-permit one-use checks preserved. No durable cross-process
replay claim. No A/B/fixture auto-dispatch, production linkage or activation. No new
experiment ID or command in this report.

## Verification / changed files / recommendation

Linux/.NET SDK10.0.403. Locked restore PASS; final Release build PASS,0 warnings/errors;
portable suite **874 passed,0 failed,0 skipped** (864 prior +10 new pure cases).
Initial test compilation exposed ambiguous xUnit throwing-lambda overloads; corrected
with explicit synchronous Action types, no assertion suppression. All prior tests
and guards retained. Windows native adapter/tests compiled only; Windows CI result
not claimed. Native CNG/filesystem/preview/owner-machine experiment NOT RUN.

Changed files:
- tools/PeripheralBatteryCompanion.EphemeralCapability/Option2Safety.cs (new)
- tools/PeripheralBatteryCompanion.EphemeralCapability/Option2Engine.cs
- tools/PeripheralBatteryCompanion.EphemeralCapability/WindowsOption2Api.cs
- tools/PeripheralBatteryCompanion.EphemeralCapability/Option2Model.cs (orphan disclosure)
- tests/PeripheralBatteryCompanion.Tests/Option2StaticAuditTests.cs (new)
- tests/PeripheralBatteryCompanion.Tests/PeripheralBatteryCompanion.Tests.csproj (pure link)
- docs/STAGE_A2_2C6_STATIC_SAFETY_AUDIT.md (this report)
- docs/STAGE_A2_2C6_OPTION2_IMPLEMENTATION_REVIEW.md (later audit cross-link)

**GO:** owner-controlled Windows checkout/status, normal non-experiment regression and
pure read-only preview only, following small separate owner steps. No blocking static
finding for those actions remains. **NO-GO:** native execution until separate owner
approval after exact reviewed preview/scope/fresh ID; strong filesystem/native behavior
and persisted-policy causality remain unresolved. No native command issued. No merge,
rebase, release, new dependency, old journal/key inspection or production activation.
Logs/TRX: `/workspace/stage-a2-2c6-static-audit-artifacts/`.

## References

- [NCryptOpenKey](https://learn.microsoft.com/en-us/windows/win32/api/ncrypt/nf-ncrypt-ncryptopenkey), [CreatePersistedKey](https://learn.microsoft.com/en-us/windows/win32/api/ncrypt/nf-ncrypt-ncryptcreatepersistedkey)
- [SetProperty](https://learn.microsoft.com/en-us/windows/win32/api/ncrypt/nf-ncrypt-ncryptsetproperty), [GetProperty](https://learn.microsoft.com/en-us/windows/win32/api/ncrypt/nf-ncrypt-ncryptgetproperty), [FinalizeKey](https://learn.microsoft.com/en-us/windows/win32/api/ncrypt/nf-ncrypt-ncryptfinalizekey)
- [SignHash](https://learn.microsoft.com/en-us/windows/win32/api/ncrypt/nf-ncrypt-ncryptsignhash), [ExportKey](https://learn.microsoft.com/en-us/windows/win32/api/ncrypt/nf-ncrypt-ncryptexportkey)
- [DeleteKey consumption](https://learn.microsoft.com/en-us/windows/win32/api/ncrypt/nf-ncrypt-ncryptdeletekey), [FreeObject](https://learn.microsoft.com/en-us/windows/win32/api/ncrypt/nf-ncrypt-ncryptfreeobject)
- [ECC blob representation](https://learn.microsoft.com/en-us/windows/win32/api/bcrypt/ns-bcrypt-bcrypt_ecckey_blob), [UI policy layout](https://learn.microsoft.com/en-us/windows/win32/api/ncrypt/ns-ncrypt-ncrypt_ui_policy)

**NATIVE NOT RUN. OWNER APPROVAL REQUIRED. PERSISTED POLICY PROOF NO-GO. STOP FOR OWNER REVIEW.**
