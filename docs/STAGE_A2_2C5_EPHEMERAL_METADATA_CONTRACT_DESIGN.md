# A2.2C5 — ephemeral metadata contract design review

**DESIGN ONLY. NATIVE EXPERIMENT NOT RUN; POLICY PROOF NO-GO.**

## Decision / scope

Recommend **Option B for a future implementation proposal, conditional on explicit
owner approval of its changed evidence contract and residual UI risk**. This is not
approval to implement it now, remove gates, or execute native code. If the requirement
is an absolute technical guarantee of no UI with flags0, Option B is NO-GO too.
Current runtime contract remains Option A without modification.

Baseline `72279fb27b6fd9a063a9bd23eb28e602a767e922`; existing feature branch
`feature/stage-a2-2c5-isolated-security-harness`; PR #13 draft, stacked on #12.
Reviewed AGENTS.md, WindowsCapabilityApi, model/engine/interop/diagnostics/Main, pure
tests, metadata investigation, export strategy, implementation and native static audit.
Only this new design document changes. This briefing prohibits publication; commit
is local only, with no push or PR edit. No merge/rebase/release/base change.

Owner evidence: operation `3bb5de0629d640569843367dd3407fc8` reached Configure and
Finalize PASS, then Metadata INCONCLUSIVE/NTE_NOT_SUPPORTED without a property tag
in that older build. Export sizing/fill never ran; no private output allocated.
Release success was harness-reported, not independent resource verification. Operation
is retired; older `3c4a7c190ef34f2e90625276aa9cbfdb` and its journal are untouched.
No ID generation, preview, native run, journal or owner-filesystem access here.

## Sources and documented boundaries

[R1: Key-storage property identifiers](https://learn.microsoft.com/en-us/windows/win32/seccng/key-storage-property-identifiers)
is the principal source for every matrix row below. It explicitly says UI Policy
"only applies to persistent keys"; its Export Policy description says "for a
persisted key" and "only applies to keys". Those are different strengths of wording.
Neither permits assuming a successful ephemeral getter or treating unsupported as
an actual policy value.

[R2: NCryptCreatePersistedKey](https://learn.microsoft.com/en-us/windows/win32/api/ncrypt/nf-ncrypt-ncryptcreatepersistedkey)
documents NULL key name as an ephemeral key not persisted; omission of the machine
flag means current user. The native function's name does not force persistence.
Creation returns a key that must be finalized before use; success plus controlled
arguments is construction evidence, not a disk inventory or general OS integrity proof.

[R3: NCryptSetProperty](https://learn.microsoft.com/en-us/windows/win32/api/ncrypt/nf-ncrypt-ncryptsetproperty)
and [R4: NCryptGetProperty](https://learn.microsoft.com/en-us/windows/win32/api/ncrypt/nf-ncrypt-ncryptgetproperty)
are object/property-specific operations. SET success does not promise GET support,
post-finalization readback, or effective policy enforcement. GET NTE_NOT_SUPPORTED
means the specified property is unsupported for that object, not evidence of a
secure value. Buffer/malformed/other errors also do not establish a value.

[R5: NCryptFinalizeKey](https://learn.microsoft.com/en-us/windows/win32/api/ncrypt/nf-ncrypt-ncryptfinalizekey)
completes key generation. Relevant configuration precedes it; no second finalization,
NO_KEY_VALIDATION, legacy store, overwrite, machine or recovery operation proposed.
R1 specifies UI Policy can be set only during key generation, read-only after
finalization, and limits it to persistent keys. Current ephemeral SET returning
success on owner's Windows does not establish documented ephemeral semantics.

[R6: NCRYPT_UI_POLICY](https://learn.microsoft.com/en-us/windows/win32/api/ncrypt/ns-ncrypt-ncrypt_ui_policy)
defines version1, strong-key UI flags and title/name pointers. Flags0 does not provide
an absolute no-dialog guarantee. [R7: NCryptExportKey](https://learn.microsoft.com/en-us/windows/win32/api/ncrypt/nf-ncrypt-ncryptexportkey)
defines the ECC private blob and per-provider flags. NCRYPT_SILENT_FLAG requests no
UI, with NTE_SILENT_CONTEXT if UI is required; that is a different execution condition
from flags0. R3/R5 also document silent behavior for their operations; one silent
export call does not cover every earlier lifecycle call.

Facts above are documented. The specific failing property, why it fails on this
OS/KSP, actual stored ephemeral export policy, and whether a dialog would appear
are **unknown**. Do not turn a hypothesis into an observed native result. Software
KSP and current-user context do not establish tamper-proof provider identity, ACL
protection or safety against privileged/same-user code.

## Gate-by-gate contract matrix

Current calls are in `WindowsCapabilityApi.ConfigureExportableSigningOnly` and
`Key.ReadMetadata` (baseline lines47–73). All GETs are after finalization; UI, export
and signing SETs are before. R1 is source-linked above for every row. "Key general"
is not a guarantee of support on every provider/lifecycle/version.

| Gate / security objective | Current SET / GET / validation | Applicability, failure modes, required evidence | Proposed B decision (not implemented) |
| --- | --- | --- | --- |
| Provider Name: prevent provider substitution | Open fixed Microsoft Software KSP; provider GET Name, exact equality | Object Name generic; no explicit ephemeral restriction. Provider-specific Name support/value still must be observed. Open handle origin and same-handle creation supplement, never replace missing required readback | Retain exact provider Name GET. Unsupported remains INCONCLUSIVE/stop; no fallback or claim of authenticated provider binary |
| Algorithm Name: exact P256 operation | Create ECDSA_P256; key GET Algorithm Name equals ECDSA_P256 | R1 key-general. Metadata mismatch/unsupported cannot be inferred correct from requested algorithm | Retain GET and equality; unavailable/malformed fails closed |
| Algorithm Group: prevent wrong algorithm family | Key GET Algorithm Group equals ECDSA | R1 key-general, Microsoft KSP lists ECDSA. Existing short-circuit means group may not be called if Name differs | Retain GET. Wrong name/group stops; no alternative curve/provider |
| UI Policy: no unexpected strong-key interaction | SET version1/flags0/NULL pointers; GET full versioned native structure, flags0 | Explicitly persistent-only; ephemeral SET/GET requirement is not documented. SET success not readback/no-UI proof. Unsupported is plausible, not confirmed cause | Remove BOTH SET and GET only under new approved contract. Report PersistedOnlyNotAssessed, never NoUiPolicy=true. Replace claimed evidence with explicit supervised UI-risk posture; strict no-UI guarantee remains unavailable |
| Key Name: ensure no persisted named container | Create with NULL name; GET Name successfully empty | Name generic, no universal ephemeral Boolean or promised empty-name getter. R2 explicitly documents NULL-name semantics. Unsupported/empty read alone cannot prove absence of persisted resources | Replace required Name GET with audited NULL-name/flags0 creation evidence and successful owned handle acquisition. No Name read in B; report GetterNotAssessed, not EmptyNameVerified. No unique names, enumeration or disk inspection |
| Length: exact P256 size | Key GET Length=256 | R1 key-general DWORD. Missing, malformed/overflow or wrong bit count cannot pass | Retain exact readback256; no reliance on request alone |
| Export Policy: deliberate exportable positive control | SET3, no archiving; GET actual3 | R1 describes persisted-key policy; ephemeral readback not a documented universal guarantee. SET establishes requested/configuration-return evidence only; native successful raw export demonstrates capability, not cause | Retain explicit SET3 before finalize as mandatory; omit GET by predeclared contract, never as an error fallback. Report Requested=3, SetReturnedSuccess, ReadbackNotAssessed. Capability PASS additionally requires fixed successful export/validation/cleanup. No ActualPolicy=3 claim |
| Key Type: prevent machine context | Creation flags0; ordinary owner; GET Key Type=0 | R1 key-general; absent machine flag means current user. No ACL/cross-user proof. Unexpected flags or unsupported cannot be silently reinterpreted | Retain GET0 and context/creation guards; no machine or admin fallback |
| Key Usage: keep signing-only | SET2; GET Key Usage=2 | R1 key-general. Setter success not actual usage readback or export permission | Retain SET/readback2; failure/mismatch stops |

B does not automatically fix all possible failures: Provider Name, Algorithm, Length,
Type or Usage may still be unsupported. Each retained requirement remains mandatory.
No "try strict then relax" behavior; future contract selection must precede native
calls and be visible in preview/consent/result. Existing A cannot silently become B.

## Evidence separation, exportability and unnamed construction

A useful positive-control conclusion is bounded: "this documented creation/configuration
path on this provider/runtime returned a structurally valid raw P256 private blob".
Do not rename that as "provider enforced export policy". Requested configuration,
SET returned success, GET readback, observed export and causal proof are separate
fields. Under B, actual export-policy readback is deliberately NOT ASSESSED, never
synthesized from SET3 or export success. If required SET3 itself is unsupported,
stop; no automatic fallback. Unsupported target export stays inconclusive regardless
of a positive control's result.

Dropping Export Policy GET would change the accepted evidence level, not merely
repair formatting. Owner must approve that a capability-only experiment does not
require actual policy DWORD attestation. If actual readback3 is a non-negotiable
objective, retain A; neither exports nor documentation can substitute for that missing
native evidence. No policy getter error may become success or "not applicable" on
its own. Provider-specific support research remains possible without a native rerun.

NULL-name construction is documented ephemeral evidence when the reviewed native
call is controlled: exact NULL argument, no machine/overwrite/persistent-key name,
no open/import/store APIs, one fresh valid owned output, no alternative path. That
replaces an unsupported redundant getter, not a proof of disk absence from scanning.
Current-user identity plus flags0 and retained Type0 are complementary scope evidence,
not restricted ACL or per-user isolation proof. Caller-set .NET ephemeral markers,
Unique Name/file-name properties, key enumeration and disk inventory add no trusted
replacement here and are forbidden by proposed scope too.

## Three contract options / owner approvals

| Option | Changes / expected evidence | New risks and STOP criteria | Exact approval needed |
| --- | --- | --- | --- |
| A — keep all current gates | No changes; requires actual full current readbacks including UI, Name and Export Policy | Documented applicability mismatch may prevent progress. Unsupported/malformed/mismatch => stop; no export/zeroization claimed | None to retain code. Any native run still requires fresh separate authorization; none requested |
| B — explicit ephemeral capability contract | UI SET/GET absent; Name and Export Policy GET absent by design; construction/SET/observed-export fields separated; other gates, flags0/104-byte path/cleanup retained | Less readback evidence; no technical no-UI assurance. Unexpected UI invalidates evidence, stop without interaction; unknown required metadata or SET/export/release errors stop. If absolute no UI/readback3 required => NO-GO | Separate implementation brief approves each removed call/replacement label, reduced evidence scope, flags0 supervised risk and unchanged retained gates. Execution requires later review and distinct fresh-ID approval |
| C — separately designed silent-context experiment | Reconsider lifecycle flags using NCRYPT_SILENT_FLAG only where officially supported; not a variant/fallback of current flags0 comparison | Different semantics/comparability; export-only silence does not cover creation/finalization. All-call flag applicability and stop/cleanup still unresolved. Silent-context errors remain inconclusive, not proof | Separate design/implementation authorization explicitly changing flag requirement; call-by-call audit. No current approval and no present implementation GO |

**Recommendation: conditional design GO for a B implementation proposal, native
execution NO-GO.** B is minimal only if the owner accepts capability evidence rather
than property attestation and acknowledges that supervised flags0 has residual UI
risk. It avoids invoking an explicitly persisted-only control as though it applied
to ephemeral keys. It does not claim its replacement satisfies an absolute no-UI
requirement. If the owner cannot accept those limits, recommend A/no progress while
C is separately researched, rather than lowering a guarantee silently.

## Distinct no-UI posture / comparability

For B, preview and later consent would explicitly state: no technical no-dialog
guarantee; supervised ordinary user only; no interaction with unexpected provider UI;
such a run is interrupted/inconclusive, never accepted as a successful unattended
experiment. Software KSP, no named container and no requested strong-key protection
reduce configured interaction, but do not prove impossibility of UI. No attested
NoUiPolicy boolean may be invented. Owner-supervision evidence is human observation,
not automatically verifiable from the return status.

External cancellation can bypass finally. This design adds no UI automation, watchdog,
process kill, timeout or child process. Failure to complete cannot establish key
release or memory clearing. Those mechanisms require their own context/lifetime/
cleanup review, not a suggestion to execute them here.

A NCRYPT_SILENT_FLAG experiment could explore a different risk posture, but would
not reproduce earlier flags0 export semantics. Setting silent on export alone leaves
earlier calls uncovered. R3/R5/R7 support silent behavior for their calls; do not
assume provider-open/create or every property uses identical flag rules. C needs a
complete matrix before implementation. No current flags changed or trial proposed.

## Risk register / remaining questions

| Risk / unknown | Current disposition / mitigation in future proposal |
| --- | --- |
| Old metadata error's exact property unknown | Keep hypothetical attribution; do not rerun either retired operation or access retained journal |
| Ephemeral UI control is persisted-only | A remains blocked if unavailable; B needs explicit removal/risk decision, no fabricated true value |
| Export Policy ephemeral support ambiguous | B narrows evidence, not interpretation of errors; actual value not assessed; GET retained if attestation required |
| Provider Name/other key getters may be unavailable | Required B reads still stop. No universal-support assumption or cascading gate removals |
| Flags0 unexpected dialog or block | Owner review of residual risk; no technical no-UI claim or automatic cancellation |
| NULL-name evidence depends on controlled caller/trusted native provider | Static/pure creation-argument tests and no alternate API; no OS-compromise/disk-absence claim |
| Native output/frame may not prove mathematical key validity | Existing104-byte representation checks only; no private import/log/copy; no new mathematical validation scope |
| Provider/runtime memory, paging, dumps, process termination | Preserve limited owned-buffer clearing claims and independent release outcomes; no absolute erasure |
| Contract B conflated with strict A or persisted target | Explicit versioned result/consent and always policy NO-GO; no automatic old-fixture attachment |

Unanswered owner design questions: Is actual Export Policy readback a required goal
or only deliberate exportability/capability? Is supervised flags0 acceptable despite
possible UI, or is technical silence mandatory? Is documented NULL-name construction
acceptable without getter corroboration? These are next-design decisions, not
requests for native execution or approval inferred from silence.

## Concise next-stage implementation briefing outline (proposal only)

1. Owner explicitly chooses contract/version and approves changed evidence assertions;
   define capability-only acceptance, not persisted policy proof. No execution yet.
2. For B only if chosen: separate immutable requested/SET/readback/observation statuses;
   omit specified calls unconditionally by contract. Never catch unsupported and
   fill success, fall back to another contract or relax required reads.
3. Keep exact software provider/P256/signing2/user/NULL creation, one finalize,
   mandatory SET3, flags0, fixed ECCPRIVATEBLOB104, owned buffer/no private copies,
   zero full capacity and key/provider/partial release evidence. No certificate,
   filesystem/journal, registry, accounts, transport or target operations.
4. Pure tests: exact absence of UI SET/GET and skipped getters in B; creation arguments
   NULL/flags0; requested/set versus actual/readback distinction; unsupported retained
   getter and SET3 stop; no export before all retained gates; unsupported target never
   accepted; no UI/readback success inferred; malformed/size/partial-fill/cleanup
   failures; contract mismatches/replay workflow/retired IDs; redacted output. Preserve
   prior A assertions through explicit separate-version tests, do not delete them.
5. Static audit native signatures, metadata scope, lifecycle, safe ownership, no private
   copies, flags/UI disclosure, zeroization and final result semantics. Locked restore/
   build/portable tests only, then STOP for owner implementation review.
6. A later independent native authorization would need reviewed exact scope and fresh
   preview-generated operation ID. No such ID/preview/command is supplied or executed
   in this stage. Never reuse the two retired IDs or restart old fixtures.

## Checks and delivery boundary

Only read-only source/PR/documentation research and documentation diff checks. No
build/tests run for this documentation-only change; historical701 portable passes
at baseline remain historical, not native evidence or a fresh result. No executable
code/tests modified. No native API, preview, generation/export/deletion, certificate,
journal, Android, production, cleanup, merge/rebase/release or publication.

Local documentation commit only under this brief's no-publication rule; draft PR #13
remains unchanged at the reviewed baseline and on #12. The standing usual push
workflow is paused for this stage's explicit publication restriction. STOP for owner
review of the design; no runtime gate bypass or new execution authorization.

**NATIVE EXPERIMENT NOT RUN; POLICY PROOF NO-GO.**
