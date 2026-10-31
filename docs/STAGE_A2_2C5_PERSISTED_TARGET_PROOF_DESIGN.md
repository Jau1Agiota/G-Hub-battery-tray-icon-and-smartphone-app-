# A2.2C5 — future persisted-target proof decision/design

**DESIGN ONLY. No new probe implementation or native execution authorized.
Persisted-key policy proof and production activation remain NO-GO.**

Recorded2026-10-30 (America/Sao_Paulo). Source baseline
`9258287beddf38962221c0c766350d0e1c3edd4b`; [B1 owner evidence closure](STAGE_A2_2C5_CONTRACT_B_NATIVE_EVIDENCE_CLOSURE.md).
AGENTS.md, strict A, B1, original persisted harness, actual tests and prior audit/
strategy reviewed. No old resource or journal inspected; no new operation ID created.

## What the positive control establishes

Owner's B1 PASS supports a bounded statement: an unnamed ephemeral software-KSP P256
key configured with SET3/signing2 could complete the fixed native ECCPRIVATEBLOB
size/fill/frame checks with flags0 in that run. Owner saw no dialog; owned buffer
clearing and handle-release success were reported. Actual export-policy, UI Policy
and key Name readbacks were not assessed. This is not a persisted nonexportable key,
ACL, certificate binding, DPAPI, TLS or physical-device test.

The earlier managed target's0x80090029 **does not become policy-denial proof** merely
because B1 now succeeds. B1 narrows the hypothesis that the format/API is unsupported
universally; it does not show it is supported for that persisted target's particular
state/settings, nor explain where the old call failed.

| Comparison item | Evidence / unknown |
| --- | --- |
| Nominal provider/algorithm | Original source selected Microsoft Software KSP/ECDSA P256; owner earlier reported reopened provider/group/size/None metadata PASS. B1 source/metadata select same nominal provider/P256. Matching alias is not verified identical provider binary/version |
| Persistence | Original was fresh named persisted key; B1 unnamed ephemeral. Different lifecycle/storage/ACL context |
| Policy | Original requested/read None; B1 requested/SET3, GET NOT ASSESSED. Not a matched actual-policy-only comparison |
| Native path | B1 directly uses NCryptExportKey with ECCPRIVATEBLOB, NULL wrapper/params, flags0. Old source uses CngKey.Export(EccPrivateBlob). Official .NET10 source uses similar underlying calls, but actual deployed crypto assembly is unknown |
| Failure phase | Old managed exception does not reveal size versus fill native call. B1 reports both success separately |
| Context/version/UI | Ordinary owner reported; exact old/new OS build, runtime/assembly/provider versions, token/state equivalence and old UI defaults not recorded. No-dialog on B1 alone cannot establish old/future behavior |
| Key validity/use | B1 frame validated; earlier original target stopped before its signature/association/ACL tests. No old-target possession or comprehensive valid-state proof follows |

Do not open or retry the old target to fill these gaps. Missing context stays unknown.
SDK10.0.402 is not a crypto runtime version; v10.0.0 source is not proof of the exact
runtime patch that executed the old managed call.

## Threat model / claims to keep separate

Trusted local owner, unmodified supported Windows/.NET/native provider, reviewed fixed
caller and no hostile same-process injection are assumptions. A software nonexportable
key is not a hardware root of trust or protection from kernel/admin/SYSTEM compromise,
memory extraction, debugger/dumps or all same-user malware. Permission to sign is not
permission to export; proving possession/usage does not prove nonexportability.

Independent dimensions: (1) exact owned target identity and persisted lifecycle,
(2) requested policy and actual persisted readback, (3) key validity/possession,
(4) capability of the exact API/blob/configuration, (5) target export observation,
(6) attribution of rejection to policy, (7) cleanup. None substitutes for another.
Any future conclusion must be limited to reviewed API/configuration and threat model;
never claim denial for all formats/principals or automatic production approval.

## Options / decision matrix

| Option | Resources / what it could establish | What it does NOT establish / decision |
| --- | --- | --- |
|1: evidence-only hold | No new resources. Preserve B1 capability PASS and all failed/unknown target evidence; research exact provider documentation and safely supplied existing version facts | No new target behavior or causality. Safest current state; NO-GO for policy proof. No command requested |
|2: fresh target-only native harness, code-first | Future separate approval for one newly named CurrentUser software-KSP nonexportable P256 signing key and isolated public ownership journal, no certificate. Fixed native helper/readback/possession/export/cleanup | Earlier B1 is a historical capability control, not exact matched context. Native unsupported/state/access error stays noncausal/inconclusive; NO-GO until reviewed proof criteria met |
|3: separately approved paired capability+target experiment | One new ephemeral SET3 control followed, only after successful clear/release, by one fresh persisted SET0 target in one reviewed process with identical fixed native export helper/provider/algorithm/flags. Separate phase results and resource approval | Better context matching, but policy AND persistence still differ. Does not isolate policy as sole cause; never relabel target0x80090029. Broader scope and another B/control run require explicit approval, not an automatic prerequisite/retry |

No option creates an exportable **persisted** comparator, mutates target policy to
make export succeed, changes provider/blob/flags, installs a cert/root or tries
formats until a preferred HRESULT appears. No fallback from2 to3 or from target to
old fixture. All three historical operation IDs remain retired.

Recommendation: retain1 now. Proposed next stage, if owner wants progress, is a
**code-only implementation/review brief for option2's minimal target harness**, with
pure tests and no native permission. Before a later native briefing, decide whether
missing version/context evidence requires option3 instead; pairing is not implicitly
approved. Even a completed target harness cannot promise that generic rejection will
establish policy causality. This document authorizes neither implementation nor run.

## Documented semantics / exact interpretation rules

- [NCryptCreatePersistedKey](https://learn.microsoft.com/en-us/windows/win32/api/ncrypt/nf-ncrypt-ncryptcreatepersistedkey):
  named key requests persistence; no machine flag means current user; no overwrite
  flag means collision returns NTE_EXISTS. NULL name gives an ephemeral key.
- [Property identifiers](https://learn.microsoft.com/en-us/windows/win32/seccng/key-storage-property-identifiers):
  Export Policy DWORD explicitly describes a persisted key; None0 has no export/
  plaintext/archiving bits. Require actual readback0 on the owned persisted target,
  not assumptions from SET success. UI Policy is persisted-only, set during generation;
  version/flags/readback and its residual UI risk need explicit target design.
- [NCryptSetProperty](https://learn.microsoft.com/en-us/windows/win32/api/ncrypt/nf-ncrypt-ncryptsetproperty),
  [Finalize](https://learn.microsoft.com/en-us/windows/win32/api/ncrypt/nf-ncrypt-ncryptfinalizekey)
  and [GetProperty](https://learn.microsoft.com/en-us/windows/win32/api/ncrypt/nf-ncrypt-ncryptgetproperty):
  set generation-time policy before one finalize, then read actual persisted metadata
  after controlled reopen. Unsupported property means unavailable evidence, never a
  safe default or alternate-policy permission.
- [NCryptExportKey](https://learn.microsoft.com/en-us/windows/win32/api/ncrypt/nf-ncrypt-ncryptexportkey):
  ECCPRIVATEBLOB is a documented ECC private representation. Query/fill must have
  separate statuses. NTE_BAD_TYPE signals incompatible blob; NTE_BAD_KEY_STATE commonly
  means incomplete/invalid key, not uniquely export policy. Return list is not exhaustive.
- [Microsoft HRESULT definitions](https://learn.microsoft.com/en-us/windows/win32/com/com-error-codes-4):
  NTE_NOT_SUPPORTED=requested operation unsupported; NTE_PERM=access denied;
  NTE_BAD_KEY_STATE=invalid specified state. None alone states policy causality.
- [Official .NET10.0.0 CngKey.Export source](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Security.Cryptography/src/System/Security/Cryptography/CngKey.Export.cs):
  managed sizing/fill wrapper with flags0 and internal allocation. That code has
  error-path/internal-buffer clearing limits. A future target should use the reviewed
  caller-owned bounded native helper, not repeat managed export with hidden buffers.

| Future observation | Classification / action |
| --- | --- |
| Wrong identity/provider/scope/policy, missing key, invalid possession, ownership or preexisting collision | FAIL; no export; no repair/adoption/overwrite |
| Required metadata unavailable or positive-control unsupported | INCONCLUSIVE/NO-GO; stop, no fallback |
| Target size/fill0x80090029 or incompatible-type error | INCONCLUSIVE export capability/policy question; NO-GO, even if control PASS |
| Target state/access rejection | Record rejection observed. Finalized metadata/possession may narrow hypotheses, but generic code alone cannot yield policy-causality PASS |
| Target returned any private bytes | FAIL nonexportability claim; clear entire allocation, stop further attempts, owned cleanup only |
| Export rejected + persisted readback0 + valid identity/possession + matched capability | Bounded rejection consistent with policy; still insufficient for unique causality without an independently justified provider-specific contract |
| Any private-output clearing/release/journal/cleanup uncertainty | FAIL or cleanup unconfirmed; never overall proof PASS |

A future design must document its evidence threshold before execution, including
whether it can report only "configured policy0 and export not observed". Do not invent
a denial-code guarantee to manufacture an enforcement PASS. Current POLICY PROOF
stays NO-GO; independent provider-specific documentation or reviewed causal rationale
is an open prerequisite, not supplied by this design.

## Proposed option2 operation order — NOT implemented

1. Separate owner approval of code-only scope/acceptance, then reviewed code/pure tests;
   only later an explicit one-run native approval with a fresh preview-generated ID.
   No ID/preview/native command is created here. Carry all three retired IDs forward.
2. Preflight ordinary unelevated owner, selected current-user software KSP/P256,
   exact bounded test directory/journal/key identifiers, collision and reparse checks,
   no preexisting object adoption. Print sanitized scope first; no admin/ACL repair.
3. Create protected test-only directory/ownership manifest with atomic fresh creation
   and owner-restricted ACLs. Record exact public planned identifiers before native
   mutation; use exclusive ownership and durable stage records. No private bytes,
   raw credentials, SID or sensitive path in shared report.
4. Create one new named user signing key with explicit policy0/no archiving before one
   finalize. Never set exportable or overwrite. A persisted key entails provider-managed
   private storage; that side effect differs from B1 and requires explicit approval.
   No application export/PFX file is permitted. UI configuration/readback for this
   persisted object must be explicitly reviewed; flags0/no-dialog limits remain.
5. Record exact public SPKI binding after creation, verify provider/algorithm/size/
   user scope/name association and actual persisted policy0/usage2 on exact reopen.
   Prove possession by a local random challenge verified with the recorded public key,
   no certificate/store install required. Unknown ACL/profile/security evidence is
   never promoted to effective isolation; no second-user test or account creation.
6. Only after every required prerequisite, attempt one fixed native ECCPRIVATEBLOB
   sizing call (flags0/NULL wrapper/params). On rejection stop export path; do not fill
   or try other formats. Only bounded expected sizing permits one caller-owned fill;
   any returned private bytes cause FAIL and full-capacity zeroization. Record exact
   phase/status; no logs/import/serialization/copy of private output.
7. Clear output before owned cleanup; verify exact live journal and public identity,
   remove only exact fresh owned persistent key, record outcome, dispose remaining
   owned handles. Never remove by prefix, subject, inventory or historical ID.
8. Report facts/unknowns and stop. No automatic proof-to-production transition, no
   repeated experiment, cert, DPAPI, TLS, pairing, firewall/router, Android or device work.

## Cleanup, interruption and journal design gates

Journal is ownership/evidence scaffolding, not credentials or anti-tamper/anti-rollback
protection from same-user/privileged code. Include schema, anonymous operation, exact
planned provider/key identity, public SPKI/hash, successful creation stages and cleanup
results; no private key bytes. Exact binding and process ownership required to delete.
A plan/prefix alone is insufficient; crash before binding means orphan/unconfirmed,
not permission to adopt or silently finish it. No backup restoration or automatic
recovery. Preserve evidence for separately approved manual review, never read old
journals under this stage. Refuse duplicate/preexisting identities before writing.

[NCryptDeleteKey](https://learn.microsoft.com/en-us/windows/win32/api/ncrypt/nf-ncrypt-ncryptdeletekey)
deletes the key AND frees its handle on success; docs permit NCryptFreeObject if
delete fails. Future safe-handle code must explicitly model that ownership transition,
invalidate/consume the handle only on confirmed delete success, and never double-free.
Delete failure/uncertain result is not retry-safe proof; record orphan/cleanup unknown,
release only remaining owned handle according to reviewed semantics, no second delete.
[NCryptFreeObject](https://learn.microsoft.com/en-us/windows/win32/api/ncrypt/nf-ncrypt-ncryptfreeobject)
is handle release, not deletion of a named persisted key. Do not confuse B1 release
success with a future persistent key cleanup requirement.

Test durable journals/exclusive ownership and interruptions before/after creation,
binding, flush, reopen, export, deletion and cleanup record. Exact fresh-key absence
check may supplement delete status only under approved owner scope; it is not broad
disk inventory. Cleanup failure never overall success. No guaranteed finally/clearing
on kill, native corruption, power loss or fatal allocation failure. Provider/runtime
copies, paging/dumps remain outside application-buffer claims. Unexpected dialog/hang
requires STOP without improvised interaction, timeout, process kill or retry; no
technical flags0 no-UI guarantee. A changed silence/cancellation design is separate.

## Proposed pure tests / review gates

Future implementation must preserve strict A/B1 tests and use fake native-call
recorders for the real new call flow, with no native tests in ordinary runner:

- All retired IDs/preexisting key/directory/journal refusal; exact scope/flags/NULL
  parameters versus deliberately named fresh target; no exportable persisted policy.
- Mandatory SET0 before finalize, actual readback0 after reopen; policy mismatch/
  unsupported/getter failures stop before export; no fictional success/defaults.
- Possession/key-public-binding mismatch, wrong provider/user/key, key loss and ACL
  unavailable; no repairs or adoptions. Mock tests never prove native ACLs/possession.
- Size unsupported, bad type/state/access errors, control PASS plus target unsupported
  stays inconclusive; no generic status becomes causal proof. One fixed format/attempt.
- Partial fill, malformed/oversized/negative/changed lengths, all private buffer
  unwind/clearing paths and no private diagnostic serialization/copies.
- Partial native acquisition, free/delete errors/throws and successful-delete handle
  consumption; no double-close/delete; provider lifetime and cleanup uncertainty.
- Durable journal interruption at each boundary, malformed/binding mismatch,
  crash-orphan/no-auto-recovery, no unrelated deletion and no prefix cleanup.
- No-dialog observation distinct from program guarantee; no UI automation/watchdog.
- Contract result cannot enable production or dispatch prior fixtures; code-only
  validation cannot produce a native success or generate an execution ID.

Native signing/export/reopen/delete/ACL and durability behavior would still need a
later approved Windows execution. Suggested next briefing must list resource inventory,
policy/causality acceptance, UI limitations, cleanup ownership transfer, code-only
checks and STOP before native approval. No such implementation approved here.

## Current verification / STOP

Documentation-only source/reference review and diff/link/content checks; no build,
automated test or native action run. Owner814 Windows regressions and B1 PASS are
attributed in closure, not newly executed Codex results. Exact changed files are the
closure, this design and one implementation-review cross-link. Source/tests and
production security/pairing/TLS/listener/Android remain unchanged. Commit/push only
documentation on existing branch; PR #13 stays draft on #12; no merge/rebase/release.

STOP for owner review. No new operation ID or execution command supplied.
**PERSISTED-TARGET POLICY PROOF NO-GO; FUTURE IMPLEMENTATION AND NATIVE EXECUTION REQUIRE SEPARATE APPROVAL.**
