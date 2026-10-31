# A2.2C6 — Option 2 persisted-key evidence harness / safety review

> Later [focused static safety audit](STAGE_A2_2C6_STATIC_SAFETY_AUDIT.md) hardens
> clearing-before-reporting and delete ownership, with874 portable passes. Historical
> results below describe the original implementation; native proof still NOT RUN.

**NATIVE NOT RUN; OWNER APPROVAL REQUIRED; POLICY PROOF NO-GO.**

## Authorization / preserved baseline

Owner authorizes code, portable tests and commit/push to existing draft PR #13 only.
Exact initial branch `feature/stage-a2-2c5-isolated-security-harness`, HEAD
`a449ef8c2724aa7659f98ac02922c527225d1a1a`, clean when inspected; remote PR head/base
verified. AGENTS.md, Option2 design, B closure, strategy, static audit, original fixture,
actual A/B source and tests read. No conflict or unrelated work overwritten.

Separate contract **PersistedKeyEvidence/Option2-v1**, test-only route in existing
console tool. No automatic A/B1/old-fixture dispatch or fallback. All existing A/B1
implementations, original persisted fixture, native signatures/helpers and assertions
are unchanged. Shared format/binding/handle helpers reused without relaxing them.
No solution, project, package, lock, production or Android dependency change.

All three retired IDs are blocked by new consent, native name guard and journal factory:
`3c4a7c190ef34f2e90625276aa9cbfdb`, `3bb5de0629d640569843367dd3407fc8`,
`6df3c74dc1804991a04730bd39aa519d`. No old key/journal or recovery export accessed.
No real experiment ID generated. Preview and native entrypoints were NOT executed.

## Contract / exact resource scope (future owner approval still required)

Exact contract/confirmation/UI-and-orphan-risk acknowledgement and valid fresh v4 ID
are required; one-use permit consumed before owner or native work. A consumed permit
cannot retry after failure; no durable cross-process ID-history guarantee is claimed.
Pure preview renderer prints only scope, not an execution command; no native/FS call.

Future resource inventory, not created in this task:

- One fresh named CurrentUser Microsoft Software KSP ECDSA_P256 signing key,
  `PBC-A22C6-Option2-<approved operation>`, policy0/no export/archiving, usage2,
  flags0/legacy0, no overwrite, import, alternate provider/format or paired control.
- One local fixed-drive owner-only operation directory and exclusive public ownership
  journal under LocalApplicationData/PBC-A22C6-Option2-Evidence. Namespace may be
  created or validated, not repaired; operation directory/file must be NEW.
- Transient RNG challenge/hash, public ECC blob/SPKI/signature/public-only verifier,
  and at most one104-byte caller-owned private-export allocation.

No certificate/root/My store, PFX, DPAPI registry, account, production identity,
listener, TLS, pairing, network/firewall/router, HID, Android or tray operation. A
provider-managed persisted private key is an intended future side effect, NOT an
application private-export file. It requires separate explicit native approval.

## Real call flow / mandatory gates

The same Option2Engine is executed through recorders in pure tests and the Windows
adapter in future native mode. It passes and validates fixed arguments, not a mock-only
high-level success flag. Windows bridge rejects wrong provider/name/policy/usage/flags.

1. Ordinary interactive authenticated unelevated owner gate, exact software provider
   flags0. Check only exact planned key name for collision; no broad enumeration.
   Only NTE_BAD_KEYSET with an invalid output counts as definitive absent key,
   matching official CngKey.Exists behavior. Generic NOT_FOUND/unsupported/access
   errors do not mean absence. Valid partial outputs are released/rejected by shared
   Native.CheckCreated. No key/journal mutation after preflight uncertainty/collision.
2. Create exclusive new restricted journal; durable Planned record BEFORE native key
   creation. Create one exact named key, user flags0/legacy0. Created record follows
   successful acquisition; incomplete/uncertain creation is an orphan, not adoption.
3. Separate evidence phases for mandatory Export Policy SET0, signing SET2 and
   persisted UI Policy SET(version1,flags0,NULL text pointers), all call flags0.
   One finalize after successful generation settings; no retry or policy relaxation.
4. Read bounded public ECCPUBLICBLOB, construct canonical public P256 SPKI, validate
   representation, durably bind it into journal. Live journal bytes/ACL/plan and exact
   provider/key Name/user scope/SPKI must match before proceeding.
5. Release original creation handle (does not delete persistent key), controlled exact
   reopen once, revalidate identity/SPKI, then actual algorithm/group/length256,
   **Export Policy GET0**, usage2 and full version1/UI flags0 structure. Unsupported,
   missing/malformed/wrong metadata stops. Requested SET0 and actual readback0 have
   distinct evidence; neither is causal export-policy proof.
6. Fresh32-byte challenge, SHA256, NCryptSignHash using owned reopened key, verify
  64-byte IEEE-P1363 signature with bound public P256 key. Public-only verifier imports
   no private parameters. Clear challenge/hash. Signing proves possession only; no cert.
7. Verify live journal again and persist ExportAttempted. Only then one fixed private
   NCryptExportKey ECCPRIVATEBLOB sizing call, flags0/NULL wrapping key/params. Any
   rejection ends that export path; no fill or alternative format. Only exact104
   permits one fill. No private copy/resize/PFX/log/serialization/disk export.
8. Clear entire owned allocation in finally **before export-outcome journal flush or
   cleanup**, including partial native failure, malformed size/frame and managed
   errors. Any successful fill or unexpected nonzero partial output causes FAIL;
   native rejection plus output cannot masquerade as denial proof. Public journal
   records rejection and, where applicable, unexpected output without bytes.
9. Cleanup requires durable binding, known usable owned handle, unchanged live journal,
   exact provider/Name/scope/SPKI again. No deleted key selected by prefix/inventory.
   One delete attempt only; failure/uncertainty never retries. Retain journal/directory.

Public binding uses documented ECCPUBLICBLOB72 (header8 + X32/Y32) and canonical
P256 SPKI91. Private attempt remains ECCPRIVATEBLOB104 (header8 + X32/Y32/d32), ECS2
magic/cbKey32. Public exports needed for identity are not alternative private-export
attempts or positive controls. Representation checks are supplemented by native/public
signature verification only in a future approved run; no math/native proof from fakes.

## Delete ownership / lifetime safety

`WindowsOption2Api.Key.DeleteOnce` serializes delete/dispose. NCryptDeleteKey success
means key deletion AND native-handle consumption. On code0 the owning SafeHandle is
immediately SetHandleAsInvalid and state becomes ConsumedByDelete; no FreeObject of
that consumed handle. Engine requires code0 AND actual consumed-state observation,
not status alone. Shared SafeHandle marshalling retains per-call reference; resource
wrappers/provider remain owned through synchronous key work. No concurrent API use is
supported or introduced.

Known nonzero delete status leaves a still-owned handle; dispose/free that handle
only, preserve journal and report cleanup unconfirmed. Unknown interop outcome marks
ownership unknown and invalidates the managed handle without a potentially second
free. This deliberately may leave an orphan/unknown resource; it is never cleanup
success. No automatic delete retry, recovery scan, old operation adoption or manual
cleanup command. Native FreeObject alone never means persisted-key deletion.

Creation/reopen partial acquisition uses existing checked helper; free success does
not prove a possibly persisted key was removed. Crash before durable binding means
orphan/no deletion. Release of creation handle must be confirmed before reopen.
Failure/absence on reopen cannot trigger a second open for recovery. Known identity/
journal failure stops deletion. Key then provider then journal close attempts are
tracked; nested finally preserves remaining cleanup attempts during normal unwind.

## Journal / filesystem review and limits

Windows adapter uses exact fresh atomic CreateDirectoryW with owner/protected DACL
at creation, rejects preexisting operation directory/file and reparse ancestors,
requires a local fixed drive, validates root/operation owner-only ACLs and stream
owner-only inherited/explicit ACL. Existing namespace is accepted only with exact
restricted observations; no repair. Journal FileMode.CreateNew, FileShare.None,
WriteThrough, Flush(true) per record. Max65536 public bytes; no unbounded read/append.

Journal fields: schema/contract, anonymous operation/exact key/provider/algorithm,
requested0, byte-offset sequence, stage, public SPKI only on binding. Pure protocol
checks permitted transitions; invalid/duplicate/completed/deletion-before-binding
states refuse. Successful flush precedes in-memory state/binding commitment; errors
mark broken and block deletion. Verify exact expected bytes/length, plan/binding,
non-reparse and fresh owner ACLs before private export and cleanup. No old journal
read/resume/replay or backup restoration exists. Crash after binding still does not
authorize a new process to adopt/delete an orphan.

Completed means exact owned-key deletion and associated public journal records were
reported successful before final resource-close evidence. It is not independent
absence-of-persistence proof or a persisted proof of the journal's own subsequent
close. Later provider/journal release uncertainty downgrades overall cleanup/result.
Keep final console evidence with journal; never infer full cleanup from generic free.

Journal is evidence, not a same-user/admin anti-tamper or anti-rollback boundary.
Checks do not promise defeat of hostile same-user path replacement/TOCTOU, kernel
compromise or lying storage. Local profile trust and exclusive owner scope are
assumptions. Atomic fresh creation + flush/strict no-adoption make partial writes
fail closed; append is not an atomic multi-sector power-loss transaction. Real ACL,
sharing, reparse and durability behavior is a **Windows validation gate**, NOT proved
by synthetic records. Unexpected state stops; no weakening to get a successful run.

## Result semantics / no causal green result

There is no overall policy-proof PASS path in this contract. Facts (readback0,
valid possession, observed export status, exact delete and record) are separate.
Sizing/fill0x80090029, BAD_TYPE, BAD_KEY_STATE or PERM is a rejection observation,
not a unique policy cause; result INCONCLUSIVE/NO-GO if cleanup is confirmed.
Other errors/invalid prerequisites/private output or cleanup uncertainty fail closed.
Any returned private output overrides rejection and is cleared. Inconclusive CLI
classification exits3; FAIL exits1; invalid consent/platform exits2. No zero exit is
reachable from the current engine's proof result; no automatic production transition.

UI Policy applies to this persisted object, unlike B1. SET/GET/version/flags remain
mandatory, but flags0 do NOT guarantee no dialog/hang or cancellation. Unexpected UI
invalidates a future run: STOP without interaction. No silent flags, watchdog,
timeout, child process, process kill or UI automation. Prior B1 owner no-dialog
observation is not this target's guarantee. No execution command supplied here.

Normal managed unwind only: private output clear precedes filesystem outcome I/O,
then cleanup; challenge/hash clear separately. Provider/runtime copies, paging, crash
dumps, native memory corruption, kill, power loss and fatal allocation failures are
outside deterministic clearing/release claims. Delete unknown-outcome handling prefers
unconfirmed leakage/orphan over a possible double-free; do not improvise recovery.

## Portable verification / remaining gates

Linux/.NET SDK10.0.403. Locked restore PASS; final Release build PASS,0 warnings/errors;
full portable suite **864 passed,0 failed,0 skipped** (767 existing +97 new Option2
cases). An initial nullable-owner compile diagnostic was corrected by explicit
missing-owner rejection; no warning suppression, skipped assertion or safety bypass.
Existing Windows test assembly/tool compiled only; no Windows-native execution.

Executed commands from repository root with configured SDK environment:

```text
dotnet restore PeripheralBatteryCompanion.sln --locked-mode
dotnet build PeripheralBatteryCompanion.sln -c Release --no-restore
dotnet test tests/PeripheralBatteryCompanion.Tests/PeripheralBatteryCompanion.Tests.csproj -c Release --no-build --no-restore --logger "trx;LogFileName=option2-portable.trx" --results-directory /workspace/stage-a2-2c6-artifacts/test-results
```

Pure recorder tests cover exact arguments/order, SET0/2/UI before finalize, mandatory
readback0 after reopen, binding/challenge/sign/verify gates, single private export,
rejection-not-causality, all length/partial-write/output/allocator clearing paths,
clearing before journal flush/delete, signature/binding/metadata failures, wrong owner,
consent/retired IDs/replay, collisions, ACL evidence, journal mismatch and all durable
stage interruptions before/after simulated flush, orphan/no adoption, native delete
consumption/nonzero/unknown/inconsistent results, no double-free, partial acquisition,
resource errors, malformed UI and redacted output. Synthetic byte patterns are NOT
actual key material. Test assembly links model/engine/protocol/security only; neither
Windows adapter, real filesystem fixture nor Main is executed.

**GATE1:** actual Windows NCrypt property/possession/export/delete/handle behavior NOT
RUN. **GATE2:** actual ACL/reparse/exclusive-create/flush/power-loss behavior NOT RUN.
**GATE3:** same-user/privileged attack resistance, independent persistence audit and
causal provider-specific export-policy proof NOT ESTABLISHED. Stop before native
approval; compilation/pure tests do not open any gate. Cross-account tests, production
provisioning, cert, DPAPI, TLS, pairing, Android/physical-device validation excluded.

## Exact implementation inventory

- tools/PeripheralBatteryCompanion.EphemeralCapability/Option2Model.cs (new)
- tools/PeripheralBatteryCompanion.EphemeralCapability/Option2Engine.cs (new)
- tools/PeripheralBatteryCompanion.EphemeralCapability/Option2JournalProtocol.cs (new)
- tools/PeripheralBatteryCompanion.EphemeralCapability/Option2JournalSecurity.cs (new)
- tools/PeripheralBatteryCompanion.EphemeralCapability/WindowsOption2Api.cs (new)
- tools/PeripheralBatteryCompanion.EphemeralCapability/WindowsOption2Journal.cs (new)
- tools/PeripheralBatteryCompanion.EphemeralCapability/Program.cs (explicit tool-only route)
- tests/PeripheralBatteryCompanion.Tests/Option2HarnessTests.cs (new)
- tests/PeripheralBatteryCompanion.Tests/PeripheralBatteryCompanion.Tests.csproj (pure links)
- docs/STAGE_A2_2C6_OPTION2_IMPLEMENTATION_REVIEW.md (this review)

Build/restore/TRX logs in `/workspace/stage-a2-2c6-artifacts/`. No asset/recovery export,
owner's untracked file, old operation/journal or original fixture changed. No reset,
clean, merge, rebase, branch rewrite or release.

## Microsoft reference audit

- [NCryptCreatePersistedKey](https://learn.microsoft.com/en-us/windows/win32/api/ncrypt/nf-ncrypt-ncryptcreatepersistedkey), [OpenKey](https://learn.microsoft.com/en-us/windows/win32/api/ncrypt/nf-ncrypt-ncryptopenkey): names/user flags/collision and exact reopen.
- [Property identifiers](https://learn.microsoft.com/en-us/windows/win32/seccng/key-storage-property-identifiers), [Finalize](https://learn.microsoft.com/en-us/windows/win32/api/ncrypt/nf-ncrypt-ncryptfinalizekey): persisted-policy/UI applicability and generation ordering.
- [NCryptSignHash](https://learn.microsoft.com/en-us/windows/win32/api/ncrypt/nf-ncrypt-ncryptsignhash): key, NULL padding at flags0, caller hash/signature buffers and32-bit lengths/status.
- [NCryptExportKey](https://learn.microsoft.com/en-us/windows/win32/api/ncrypt/nf-ncrypt-ncryptexportkey), [ECC blob](https://learn.microsoft.com/en-us/windows/win32/api/bcrypt/ns-bcrypt-bcrypt_ecckey_blob): distinct public/private representations, size/fill.
- [NCryptDeleteKey](https://learn.microsoft.com/en-us/windows/win32/api/ncrypt/nf-ncrypt-ncryptdeletekey): success deletes/frees; FreeObject allowed on failure, never both after success.
- [NCryptFreeObject](https://learn.microsoft.com/en-us/windows/win32/api/ncrypt/nf-ncrypt-ncryptfreeobject): handle release, not persistent deletion.
- [Official .NET10 CngKey.Exists](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Security.Cryptography/src/System/Security/Cryptography/CngKey.Exists.cs): exact missing-key status, not generic errors-as-absence.

New OpenKey/DeleteKey/SignHash P/Invoke uses explicit entrypoints, Unicode name where
applicable, Winapi default calling convention, native-width pointers/SafeHandles,
32-bit DWORD/status. Existing declarations unchanged. Pinned caller pointers avoid
private array marshaler copies. Native ABI/Windows signatures compiled/static-reviewed,
not validated by a Windows run. No lower-level machine/network/security setting changed.

**NATIVE NOT RUN; OWNER APPROVAL REQUIRED; POLICY PROOF NO-GO.**
