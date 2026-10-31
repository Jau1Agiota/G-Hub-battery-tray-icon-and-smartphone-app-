# A2.2C5 — export-proof strategy review (design only)

## Decision / exact baseline

**NO-GO for treating export rejection as established policy enforcement or for
rerunning the fixture. Conditional GO for a future separately briefed implementation
of an ephemeral-only capability experiment after the design gates below are approved.**
No experiment is implemented or authorized by this document.

Reviewed head: `94456d144038d4d7181891aaa3e80a6c76ce0300`, branch
`feature/stage-a2-2c5-isolated-security-harness`, draft PR #13 stacked on #12.
Actual Program.cs, FixtureConsent.cs, FixtureDiagnostics.cs, execution wrapper,
AGENTS.md and failure report were inspected. PR state/head/base verified read-only.
Only this Markdown document is changed in this stage.

Owner's retired operation `3c4a7c190ef34f2e90625276aa9cbfdb` is NEVER reusable.
Owner reported prerequisite metadata PASS, then ECC-private export threw
0x80090029; overall FAIL, twelve checks NOT RUN. Exact key cleanup was reported by
the old harness, not independently verified; journal retained locally and untouched.
No certificate association, signing, native ACL or effective-exportability success
can be inferred. Correction baseline: cloud locked restore/build PASS, 626 portable
cases PASS. Those are historical cloud results, not a new/native verification run.

## Authoritative references and what each establishes

1. [CngKey.Export, .NET 10](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.cngkey.export?view=net-10.0):
   underlying KSP controls supported formats; CryptographicException covers other
   errors, *typically* export policy. Its historical Windows Vista example explicitly
   includes EccPrivateBlob, Pkcs8PrivateBlob and OpaqueTransportBlob for Microsoft's
   software KSP. This supports choosing ECC-private as a candidate, not a promise
   that a particular key/provider/OS configuration must export or return one code.
2. [NCryptExportKey](https://learn.microsoft.com/en-us/windows/win32/api/ncrypt/nf-ncrypt-ncryptexportkey):
   BCRYPT_ECCPRIVATE_BLOB is an ECC private-key representation. NULL output requests
   length; a second call fills caller-owned output. Flags are provider-specific;
   NCRYPT_SILENT_FLAG suppresses UI and can produce NTE_SILENT_CONTEXT. Its return
   table is non-exhaustive; NTE_BAD_TYPE is an incompatible blob; NTE_BAD_KEY_STATE
   commonly means a key not completed with NCryptFinalizeKey. This directly prevents
   interpreting every bad-state response as export-policy enforcement.
3. [CNG key-storage property identifiers](https://learn.microsoft.com/en-us/windows/win32/seccng/key-storage-property-identifiers):
   NCRYPT_EXPORT_POLICY_PROPERTY is a DWORD policy. NCRYPT_ALLOW_EXPORT_FLAG=1,
   NCRYPT_ALLOW_PLAINTEXT_EXPORT_FLAG=2, archiving flags=4/8 are distinct. None means
   no allow bits observed. Plaintext raw ECC export is not equivalent to protected
   export or one-time archiving. Metadata must be read back, never assumed from input.
4. [NCryptCreatePersistedKey](https://learn.microsoft.com/en-us/windows/win32/api/ncrypt/nf-ncrypt-ncryptcreatepersistedkey):
   a NULL key name creates an ephemeral key that is not persisted; no machine flag
   means current user. Overwrite is opt-in; without it, duplicate named key returns
   NTE_EXISTS. The key is not usable until finalized. Function name does not imply
   that an unnamed key is persisted.
5. [NCryptFinalizeKey](https://learn.microsoft.com/en-us/windows/win32/api/ncrypt/nf-ncrypt-ncryptfinalizekey):
   completes key creation. Do not add NO_KEY_VALIDATION or legacy-store flags. A
   native implementation must set relevant policy before finalization, then read
   actual finalized metadata. Never finalize/repair an existing owner's key here.
6. [CngKey.Create, .NET 10](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.cngkey.create?view=net-10.0):
   unnamed keys are ephemeral; creation parameters permit explicit provider/policy.
   Use explicit parameters rather than provider/export defaults in a future control.
7. [Microsoft HRESULT definitions](https://learn.microsoft.com/en-us/windows/win32/com/com-error-codes-4):
   0x80090029 NTE_NOT_SUPPORTED = requested operation not supported;
   0x8009000B NTE_BAD_KEY_STATE = key not valid in specified state;
   0x80090010 NTE_PERM = access denied. These describe observations, not unique causes.
8. [Official .NET runtime v10.0.0 CngKey.Create implementation](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Security.Cryptography/src/System/Security/Cryptography/CngKey.Create.cs):
   creates native key, sets parameters and finalizes before returning. Thus merely
   inserting another finalization call is not an evidence-based fix for the managed
   harness. Source behavior is version-specific implementation evidence, not proof
   about the exact runtime installed on the owner's PC.
9. [Official .NET runtime v10.0.0 CngKey.Export implementation](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Security.Cryptography/src/System/Security/Cryptography/CngKey.Export.cs):
   two NCryptExportKey calls, no wrapping key/parameter list, flags zero; internal
   managed allocation, throw on second-call failure, then Array.Resize and return.
   Caller cannot zero an internal output array that was not returned, or an old
   resized allocation. This limits the existing finally, which clears returned
   arrays only. Do not claim complete all-path/whole-runtime private-memory erasure.

References were read in this review; no Windows API was executed. SDK 10.0.402 does
not uniquely identify the executing runtime/crypto assembly or OS provider version.
Current Learn includes newer Windows features and historical examples, not an
OS-build-specific contract for this observed HRESULT. Do not diagnose missing
finalization, unsupported curve/blob or policy causality from the error alone.

## Appropriateness of the selected export and version uncertainties

ECDSA P-256 private key is an ECC private key; EccPrivateBlob is a documented,
reasonable raw-private candidate for Microsoft Software KSP. Signing-only usage is
not itself an export permission. There is no evidence that switching to PKCS8, an
opaque blob, an encryption flag, a different curve/provider or re-finalizing the
persisted key is required. Do not cycle formats until an acceptable error appears.

The precise native failure may be at sizing or output fill. Provider support, key
state, policy, permissions and runtime translation can differ. The old log does not
identify that phase. Even if one Windows implementation uses NOT_SUPPORTED while
refusing a nonexportable key, generic HRESULT semantics do not establish that cause
for this execution. Unsupported target export remains INCONCLUSIVE/overall FAIL.

## Minimal evidence matrix and decision rules

Statuses apply to separate evidence dimensions. A narrow PASS never automatically
upgrades a later inference. NOT RUN stays distinct from failure/inconclusive.

| Dimension | PASS evidence | FAIL | INCONCLUSIVE / NOT RUN |
| --- | --- | --- | --- |
| A: persisted target identity/metadata | Exact freshly owned key reopened; actual software KSP/user/ECDSA/P256; finalized state evidence; policy DWORD None | Wrong key/provider/scope/size/policy or mismatched ownership | Property/read/finalization evidence unavailable; no new target authorized = NOT RUN |
| B: selected export path capability | Explicit exportable ephemeral control with verified same provider/algorithm, exact API/blob/flags/context returns well-formed expected private blob | Private output mishandled, wrong scope/policy/format | Rejection/unsupported/unexpected control error; persisted lifecycle support still not proved |
| C: target export behavior | Named state/access rejection is observed and reported only as such | Target returns any private output; clear immediately and halt | Target NOT_SUPPORTED always INCONCLUSIVE; unknown error or unavailable phase cannot pass; no target run = NOT RUN |
| D: policy/effective nonexportability | Bounded conclusion only after finalized identity, verified metadata, capability, actual target behavior and provider-specific causal justification are reviewed together | Any demonstrated unauthorized target export or weakened policy | Error alone, metadata alone, positive control alone, generic state/access rejection = insufficient |
| E: cleanup | Exact owned removal/disposal status reported with scope and limits | Cleanup failed, ownership mismatch, unexpected resource | Unknown creation/deletion outcome = unconfirmed, never overall success |

Overall proof stays FAIL/NO-GO if any required dimension is FAIL, INCONCLUSIVE or NOT
RUN. In particular B PASS + target 0x80090029 does not imply C or D PASS. An ephemeral
control differs from a persisted target in lifecycle/storage/ACLs: it is a capability
control, not an experiment changing only export policy. Never use an exportable
persisted comparator to erase that distinction; such a comparator is prohibited.

## Candidate approaches and buffer-safety decision

**Exact managed positive control (proposal only):** fresh unnamed CurrentUser
software-KSP ECDSA P256, explicit AllowExport|AllowPlaintextExport, no archiving,
no persistence, exact CngKey.Export(EccPrivateBlob) in same process/runtime context.
Verify actual metadata and ephemeral status, not just creation inputs. A successful
well-formed result establishes that this ephemeral configuration supports this path;
a failure is capability-inconclusive. It says nothing conclusive about the target's
policy causality, persisted ACLs, other formats, other users or production identity.

However, the managed API's private internal buffers cannot be cleared by its caller
on all paths (reference 9). Clearing only a successfully returned blob is weaker
than the brief's all-path requirement. Therefore **NO-GO for implementing this exact
managed probe as if full caller-owned zeroization were guaranteed**. No probe added.

**Recommended future capability-only implementation:** separately reviewed bounded
native NCryptExportKey helper owning every output allocation. One fixed documented
ECC-private blob, no wrapping key/parameter list, record sizing/fill separately;
validate positive length against a small fixed P256 budget before allocation, check
returned lengths/expected P256 structure without logging coordinates/scalar. Clear
entire capacity in finally on success, partial-write/error and malformed output;
no resizing/copies/private logs, PFX, disk, dump or serialization. Use safe handles
and single owned disposal. Provider/runtime internal copies and OS paging/crash dumps
remain outside a claim of complete erasure; owner machine must be trusted. An
ephemeral key is not a hardware-backed guarantee that secrets never touch paging.

For equivalence to the observed managed path, fixed native export flags must be 0,
with identical format/parameters, in a newly created software key with no UI policy.
If strict no-UI operation instead requires NCRYPT_SILENT_FLAG, preselect and review
that flag for both future control and target; record it as a **different execution
configuration**, not a retrospective explanation of the old managed call. No flag
fallback. Unexpected UI or silent-context failure stops, never prompts for privilege.
The next briefing must explicitly resolve this choice; this document selects no new
runtime flag and authorizes no execution.

Independent read-back of NCRYPT_EXPORT_POLICY_PROPERTY through a reviewed native
adapter is useful corroboration of managed ExportPolicy, not an independent proof
of effective enforcement (same underlying provider). Do not read the retired key or
journal to obtain it. Additional certificate, ACL or signing work does not resolve
an unsupported export by itself and is outside the next minimal experiment.

## Smallest next experiment: exact scope and gates (NOT authorized)

1. Owner approves a separate implementation brief: capability-only ephemeral key,
   bounded-buffer helper and fixed API/blob/flags, safe-handle/zeroization review,
   pure failure-injection tests. No existing fixture proof replacement or target
   execution bundled with it. Resolve managed-equivalence versus no-UI flag choice.
2. That implementation must provide a new pure scope preview (not run here) and
   separate exact opt-in; fresh preview-generated v4 operation ID, ordinary unelevated
   Windows owner. Never use the retired ID. Check Windows build, process architecture,
   actual runtime/crypto assembly version and provider/algorithm metadata; share only
   sanitized version/configuration fields, no SID/path/key inventory.
3. Resource mutation scope: **one unnamed ephemeral software-KSP P256 key** with
   explicit plaintext-exportable policy in memory; one bounded private output buffer.
   NO persistent target, key container name, certificate/store write, fixture directory,
   ownership journal, accounts, DPAPI, network, root/machine scope or production data.
   No use of existing Invoke/Show scripts: their scope includes persisted key/leaf,
   so they are not this proposed experiment. No directories or manifests created here.
4. Read back finalized provider/group/size/policy/ephemeral metadata, run one export
   path with phase-specific statuses, clear owned private buffers on all paths, free
   owned handles only. No retries, alternative format/provider or relaxed policy.
   The narrow experiment is complete only if its buffer/disposal status is explicit.
5. Cleanup has no persistent-resource deletion/adoption. Crash releases process-owned
   handles via OS process teardown but cannot promise deterministic application
   zeroization; classify interrupted evidence INCONCLUSIVE, no automatic rerun. If any
   persistent artifact unexpectedly appears, STOP, report unexpected persistence,
   do not scan/adopt/delete by prefix. No orphan/journal recovery for the retired run.
6. STOP after owner results. A capability PASS only supports reviewing a **later**
   fresh nonexportable-target experiment with separate consent/ownership/cleanup.
   It does not authorize rerunning the current full key/certificate fixture. A
   capability failure calls for targeted phase/version diagnostics, not more formats.

## Current reporting review / concrete future cases

Unsupported and unexpected results currently remain failures; returned private bytes
fail after clearing. State/access rejection is narrowly labeled observed rejection,
not policy causality. Remaining risk: a future run could reach `RESULT: PASS
(isolated proof only)` on generic rejection and readers might overinterpret it.
Future reporting should split metadata/capability/rejection/causality explicitly and
say policy proof INCONCLUSIVE unless its stronger criteria are met. Do not change
current code in this design-only stage or equate a fixture PASS to deployment GO.

Cleanup wording now separates created-then-cleaned, never-created and uncertain;
key-only cleanup says leaf-not-created and by-harness/not-independently-verified.
A planned leaf is not proof it was installed; absence after exact selection/removal
is not proof of every certificate store or privileged isolation. Journal retention
is intentional. A failure after deletion but before journal recording stays
unconfirmed. Same-user tampering and broad effective-access claims remain excluded.
Operation IDs sometimes cover multiple subcalls, and public-SPKI extraction after
key creation may still be labeled CreateKey; future native phases should be finer.

Future pure/failure-injection cases before any owner native run: sizing unsupported;
fill unsupported after partial buffer write; invalid/oversized/changed lengths;
zero full output capacity on every exit; exported bytes always fatal on target;
state/access errors without capability never count as policy proof; metadata mismatch;
NOT RUN versus INCONCLUSIVE propagation; key creation failure versus successful
creation/disposal; no double-close/delete; cleanup/journal failure never overall PASS;
retired-ID and wrong-owner guards preserved; output sanitization. Simulated cases
validate software logic only, never native provider/ACL/export behavior.

## Verification / STOP

Documentation-only: no build or automated test run was needed or executed. Read-only
source/PR inspection and HTTPS research only. Prior 626 portable results are baseline
history, not this stage's tests. Native security tests, fixture/preview scripts,
CNG generation/export/deletion, certificates, journals and owner resources were NOT
RUN or accessed. Android, production source, snapshot-v1, battery/tray behavior,
untracked work and recovery exports unchanged. No merge/rebase/release/cleanup.

Review this document first. Needed before any future experiment: owner approval of
capability-only scope, fixed flags/API and buffer/disposal design, plus sanitized
Windows/runtime version context if already available. Do not run a command to gather
native evidence under this briefing. Evidence remains insufficient for policy
causality. Await a separate implementation/authorization brief; stop here.
