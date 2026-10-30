# A2.2C5 — Contract B implementation review

> Later evidence: [owner Windows B1 capability closure](STAGE_A2_2C5_CONTRACT_B_NATIVE_EVIDENCE_CLOSURE.md).
> Implementation-stage native NOT RUN statements below describe the earlier Codex
> delivery. The owner subsequently reported B1 PASS/no dialog; policy proof remains NO-GO.

**CODE / PORTABLE TESTS ONLY. NATIVE EXPERIMENT NOT RUN; OWNER AUTHORIZATION REQUIRED FOR ANY FUTURE RUN; POLICY PROOF REMAINS NO-GO.**

## Authorization / baseline

Owner replied "yes" to the three implementation decisions: separate B scope, reduced
readback evidence, and flags0 residual UI risk, for code and portable tests only.
This does not authorize a native preview, experiment or execution command. No native
entrypoint or security operation was run. Ordinary review workflow pushes this stage
on the existing feature branch; no merge, release or PR base change.

Published source baseline: `72279fb27b6fd9a063a9bd23eb28e602a767e922` (701 portable
passes). Local design parent: `e38435db0405f1778c0ec156fb7a220437ff3a6d`. The existing
local design commit accompanies this implementation when the branch is pushed; its
previous stage was local-only, while the present stage follows the standing publish-
feature-branch workflow. No history rewriting or new branch required.

Reviewed AGENTS.md, design/proposal artifact, export strategy, static audit, metadata
investigation and actual code/tests. Both retired IDs remain blocked by the unchanged
shared ID guard. No owner's key, certificate, journal, prior artifact or recovery export
was accessed/modified. Original persisted fixture, Android and production tray/polling/
HID/snapshot/transport code are unchanged.

## Distinct contract / no fallback

Contract ID: **EphemeralCapability/B1**. New model, engine, native call-flow and Windows
adapter are separate from A. Existing A model/engine/Windows adapter/native definitions,
interop contracts, metadata diagnostics and all original assertions are byte-unchanged.
A's UI/Name/actual-policy checks still reject unavailable/mismatching evidence.

Program adds explicit B-only preview and execution routing before the unchanged A
routing. A B request returns from the B branch on success, failure or invalid consent;
no exception/unsupported metadata can dispatch A or relax A into B. B has its own
exact contract/version/confirmation and explicit flags0 UI-risk acknowledgement.
Old A consent cannot grant B approval. No CLI invocation is supplied in this report.
Neither the preview nor the dispatcher was executed by Codex.

A B permit is consumed atomically before owner/native context access and cannot be
reused even after a failure. Invalid/non-v4/retired IDs are rejected first. This is
one-use per permit object, not durable ID history: another process/object can be
constructed with the same ID, so fresh preview-generated ID and human scope approval
remain workflow requirements. No journal/database or claim of cross-process replay
protection. No ID was generated for a native run in this stage.

## Exact removed versus retained calls

B omits these four calls **unconditionally by selected contract**, never in response
to an error: UI Policy SET; UI Policy GET; key Name GET; Export Policy GET. Provider
Name GET remains mandatory and is not the same as key Name GET. No unsupported result
is reinterpreted as success or silently not-applicable.

Retained native call path:

1. Ordinary interactive authenticated unelevated current-user check (same A context
   implementation). Exact Microsoft Software KSP open, flags0.
2. Create exactly one ECDSA_P256 key using that same owned provider; literal NULL
   key name, legacySpec0, flags0. No named/machine/overwrite/open/import alternative.
3. Mandatory Export Policy DWORD SET3; mandatory Key Usage DWORD SET2, both flags0;
   one finalization with flags0. Setter failure stops; no reconfiguration/retry.
4. Actual required GETs: provider Name equals software KSP, Algorithm Name ECDSA_P256,
   Algorithm Group ECDSA, Length256, Key Type0, Key Usage2. Unsupported remains
   INCONCLUSIVE; malformed/mismatch/other errors fail and prevent sizing.
5. One ECCPRIVATEBLOB sizing call with flags0, NULL wrapping key and parameter list;
   only exact104 permits output allocation. At most one fill with same fixed arguments.
   Wrong length/header fails; no alternative format/flags/provider, resize or retries.
6. Clear full owned output before exact key/provider release, with separate partial-
   acquisition/native-release/error evidence. No DeleteKey, certificate, files or
   recovery scan. Cleanup cannot convert a failed export into PASS.

ContractBNativeFlow supplies the fixed real arguments and is the same class executed
by recorder tests. WindowsContractBApi validates these constants/subsets again before
calling the existing checked Native P/Invokes. No signature or ABI change is introduced.
B read bridge allows only the six retained property IDs; B SET bridge permits only
policy3 and signing2. No UI call exists in the B native path.

## Evidence fields / acceptance

| Evidence | B meaning |
| --- | --- |
| Requested policy |3 by contract, not a readback |
| SET result |Only PASS after actual setter returned successfully; failures/not-run remain separate |
| Actual export policy value/readback |NOT ASSESSED BY CONTRACT, typed actual-value property stays null |
| UI Policy SET/GET |NOT ASSESSED BY CONTRACT; no NoUiPolicy=true field/value |
| Key Name GET |NOT ASSESSED BY CONTRACT; no EmptyNameVerified claim |
| Ephemeral intent |Validated NULL-name/user-flags0 construction arguments and successful owned creation |
| Observed export |Separate sizing, fill and104-byte ECS2/cbKey32 frame evidence, not policy causality |
| Memory/releases |NotAllocated versus full-capacity clearing; key/provider/partial release outcomes independent |
| Capability PASS |Only B's bounded ephemeral API/blob capability; never strict A, target, ACL or no-dialog proof |
| Policy proof / production activation |Always NO-GO |

NULL name is documented by Microsoft as ephemeral construction. It is not proof of
disk absence, mathematical key validity, provider integrity under compromise, user
ACL isolation or actual policy3 readback. The deliberately exportable control may
return a private blob successfully; this says nothing about enforcement on a
persisted nonexportable target. Such a target remains outside B's interfaces/path.

Unexpected UI invalidates a run. Flags0 cannot guarantee no dialog or a prompt-free
return. Preview/output disclose supervision/no-interaction STOP and no automatic
cancellation guarantee. Code cannot reliably observe external provider UI, prevent
a blocking synchronous call or verify human observation. No silent flag, watchdog,
timeout, child process, process kill or UI automation was added. Future native
approval must separately accept reviewed scope and exact fresh ID; no run requested.

## Ownership / zeroization / static review

Shared checked SafeHandles, Native.CheckCreated and failure categories are unchanged.
B wrappers hold provider/key resources through synchronous work; engine releases key
before provider. No concurrent wrapper sharing is supported. Partial failure with an
output handle is rejected/released by the same checked native helper, never adopted;
unknown release keeps overall FAIL. SafeHandle release failure is not proof that an
OS resource was freed and is not retry-safe. No second native free is attempted.

Windows B bridge retains native-width handles/pointers, fixed32-bit sizes/status,
Unicode existing declarations and flags0. Fill pins caller Span directly; no private
marshalling array/copy/serialization/log/PFX/disk output. Structural validator/104-byte
constant shared with A; B's separate unwind is independently tested for parity.
Nested finally ensures provider cleanup is attempted after key cleanup even if normal
reporting/unwind fails. Normal managed-unwinding guarantees only: catastrophic memory
failure, native corruption, external termination, provider/OS internal buffers, paging
and dumps remain outside deterministic clearing/cleanup claims.

## Pure tests / executed verification

Linux, .NET SDK10.0.403. Locked restore PASS; Release solution build PASS,0 warnings/
errors. Portable suite **767 passed,0 failed,0 skipped** (701 prior +66 new B cases).
Native adapter/tool and existing Windows test assembly compiled only. No Main,
preview, CNG generation/export/free, old fixture or native test executed.

New pure tests use the actual B call-flow with a fake native-call recorder; record
provider/algorithm/NULL/legacy/flags, mandatory setters, all retained reads, export
arguments, capacity and release order. The exact call log excludes all four omitted
calls, including cleanup/error routes. Test results verify application flow, not
actual Windows provider execution or P/Invoke marshalling.

Cases cover success without policy promotion; all retained unsupported/malformed/
mismatching metadata; SET failures; size/fill unsupported; all invalid length classes;
bad magic/width; partial writes/managed errors; bad allocator capacity; release errors/
exceptions; partial acquisition and failed release; silent-context/state/access errors
without flags fallback; one-use permit/contract/consent/retired IDs; construction
checks; repeated method guards; redaction and explicit not-assessed output. Extra
strict-A regression checks confirm UI/Name/actual-policy gates remain required; all
original tests/assertions retained. No synthetic byte patterns are actual keys.

## Exact changed-file inventory (implementation commit)

- tools/PeripheralBatteryCompanion.EphemeralCapability/ContractBModel.cs (new)
- tools/PeripheralBatteryCompanion.EphemeralCapability/ContractBEngine.cs (new)
- tools/PeripheralBatteryCompanion.EphemeralCapability/ContractBNativeFlow.cs (new)
- tools/PeripheralBatteryCompanion.EphemeralCapability/WindowsContractBApi.cs (new)
- tools/PeripheralBatteryCompanion.EphemeralCapability/Program.cs (explicit B routing)
- tests/PeripheralBatteryCompanion.Tests/ContractBCapabilityTests.cs (new)
- tests/PeripheralBatteryCompanion.Tests/PeripheralBatteryCompanion.Tests.csproj (pure links)
- docs/STAGE_A2_2C5_CONTRACT_B_IMPLEMENTATION_REVIEW.md (this review)

No new project, package, lock-file, solution, production UI or native signature change.
Previous local design document is a separate preserved parent commit. Build/TRX logs
retained at `/workspace/stage-a2-2c5-contract-b-implementation-artifacts/`.

## References / STOP

- [Approved design options](STAGE_A2_2C5_EPHEMERAL_METADATA_CONTRACT_DESIGN.md)
- [Metadata investigation](STAGE_A2_2C5_METADATA_FAILURE_INVESTIGATION.md)
- [Native static audit](STAGE_A2_2C5_NATIVE_INTEROP_STATIC_AUDIT.md)
- [NCryptCreatePersistedKey NULL-name semantics](https://learn.microsoft.com/en-us/windows/win32/api/ncrypt/nf-ncrypt-ncryptcreatepersistedkey)
- [Property applicability / policy wording](https://learn.microsoft.com/en-us/windows/win32/seccng/key-storage-property-identifiers)
- [NCryptExportKey](https://learn.microsoft.com/en-us/windows/win32/api/ncrypt/nf-ncrypt-ncryptexportkey)

STOP for owner code review. No native execution command supplied, no physical or
hardware validation claimed. No persisted-target probe, certificate, journal,
DPAPI/TLS/pairing, Android, firewall/network, merge/rebase/release authorized.

**NATIVE EXPERIMENT NOT RUN; OWNER AUTHORIZATION REQUIRED FOR ANY FUTURE RUN; POLICY PROOF REMAINS NO-GO.**
