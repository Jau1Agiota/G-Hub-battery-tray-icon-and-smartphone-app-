# A2.2C5 — Contract B owner-native evidence closure

## Classification and attribution

**Owner-observed EphemeralCapability/B1 capability PASS. Persisted-key POLICY PROOF
REMAINS NO-GO.** This closes the bounded capability experiment only, not the security
prerequisites stage, production activation or any persisted target's export policy.

Evidence recorded: **2026-10-30, America/Sao_Paulo** (closure/receipt date).
Actual experiment date/time was not supplied; it is not inferred from this date.
Source: owner's supplied Windows terminal transcription and explicit statement that
**no cryptographic-provider dialog appeared during this run**. Codex did not execute,
observe remotely or independently reproduce the native experiment.

- Branch: `feature/stage-a2-2c5-isolated-security-harness`.
- Owner-verified experiment HEAD: `9258287beddf38962221c0c766350d0e1c3edd4b`.
- Contract: `EphemeralCapability/B1`.
- One approved operation: `6df3c74dc1804991a04730bd39aa519d` — now retired.
- Platform: Windows, ordinary unelevated owner account, owner-reported.
- SDK: .NET **10.0.402**, owner-reported.
- Executing .NET runtime/crypto assembly version, Windows build and process architecture:
  **not supplied**. SDK version does not determine these; do not fill them by inference.
- [Draft PR #13](https://github.com/Jau1Agiota/G-Hub-battery-tray-icon-and-smartphone-app-/pull/13), stacked on #12.

## Evidence ledger

| Evidence | Result / limit |
| --- | --- |
| Standard Windows regression | Owner reports restore PASS, Release build PASS,814 passed,0 failed/skipped using existing test.ps1 runner. Separate from native experiment; not rerun by Codex |
| B consent/context/provider/create/construction | Reported PASS; exact approved operation, NULL-name/user flags0 construction. Not an independent disk-persistence audit |
| Export-policy request/SET | Requested3 and setter PASS. Actual policy readback NOT ASSESSED BY CONTRACT |
| Signing SET/finalize/retained metadata | Reported PASS; source checks provider/P256/group/length/type/usage. These are tool observations, not kernel/provider-integrity proof |
| Size/fill/structure | Reported PASS for one fixed NCryptExportKey ECCPRIVATEBLOB path, flags0,104-byte ECS2/cbKey32 frame. No private bytes stored in report; no mathematical private-key validity claim |
| Zeroization | Reported ClearedFullCapacity/PASS for caller-owned allocation during normal unwind. No claim about native/provider/runtime internal copies, paging, dumps, crashes or absolute erasure |
| Key/provider release | Reported PASS/0x00000000 from owned release calls. Not independent proof of absence of persistence or all OS resources |
| UI Policy SET/GET and key Name GET | NOT ASSESSED BY CONTRACT; do not invent NoUiPolicy=true or successful getter evidence |
| Dialog observation | Owner explicitly saw no provider dialog in this one run. Not a technical no-dialog guarantee for future runs |
| Persisted target/ACL/cert/DPAPI/TLS/pairing/Android | Not tested or authorized by B1; production and policy proof NO-GO |

The standard runner's814 automated tests do not constitute native cryptographic or
physical-device verification. Native B1 results come from the separate owner-approved
experiment. Historical cloud767 portable passes are a different execution/platform,
not a contradiction and not new results in this documentation task.

## Source review confirming interpretation

Reviewed actual ContractBModel/Engine/NativeFlow, WindowsContractBApi, Program, strict
A code, original persisted fixture, related tests, strategy, failure investigations,
metadata design and static audit. B calls no UI Policy SET/GET, key Name GET or Export
Policy GET. Its raw flow uses exact software provider/P256, literal NULL key name,
flags0, mandatory policy SET3/signing SET2, one finalize, required retained getters,
fixed104-byte native size/fill and full caller-buffer clearing before owned releases.

The no-dialog statement is an owner observation; code still correctly says
provider-UI-possible=true. Those statements describe different things. Similarly,
SET3 returning success and a successful export do not provide an actual policy
DWORD readback or demonstrate policy-enforcement causality.

## Retired operations / preserved evidence

| Operation | Owner evidence / status |
| --- | --- |
| `3c4a7c190ef34f2e90625276aa9cbfdb` | Persisted managed export returned0x80090029; inconclusive, not nonexportability proof. Exact cleanup/journal retention reported, not independently verified |
| `3bb5de0629d640569843367dd3407fc8` | Strict ephemeral A metadata returned0x80090029 before export; failed property unconfirmed. Releases reported success |
| `6df3c74dc1804991a04730bd39aa519d` | B1 capability PASS; no dialog observed by owner; scope and limits above |

All three IDs are retired by owner workflow: never reuse, retry, recover, adopt or
manually clean their resources. Prior journal untouched. This documentation stage adds
no executable ID guard: current source hardcodes the two older IDs, with per-permit
one-use behavior but no durable cross-process ID history. The new retirement is a
workflow rule recorded here, not a claim the binary independently enforces it. Future
approved implementation must explicitly carry all retirements forward.

## Documentation-only delivery / next boundary

New closure report and [separate future-proof design](STAGE_A2_2C5_PERSISTED_TARGET_PROOF_DESIGN.md),
plus a historical-result cross-link in the implementation review. No executable or
test file changed. Read-only source/Microsoft reference review and documentation-only
checks; no build/tests, preview/native operation, key/certificate/store action, journal
access, cleanup, merge/rebase/release or production change in this task.

Closure does not authorize another B1 run or target probe. Future implementation and
native tests each require a separate owner-approved briefing, scope and fresh exact
operation ID. No new ID or execution command is provided. STOP for owner review.

## Exact native output supplied by owner

The following status-only transcription is copied from the owner briefing. It contains
no private key material, secrets or sensitive local paths.

```text
contract=EphemeralCapability/B1; capability=Pass; ephemeral capability only
requested-export-policy=3; SET=Pass; export-policy-readback=NOT ASSESSED BY CONTRACT; actual-policy-value=NOT ASSESSED
UI-Policy-SET/GET=NOT ASSESSED BY CONTRACT; key-Name-GET=NOT ASSESSED BY CONTRACT; no NoUiPolicy=true claim
phase=Consent; status=Pass; category=B1WithExplicitUiRiskAcknowledgement; property=None; HRESULT=none
phase=OwnerContext; status=Pass; category=None; property=None; HRESULT=none
phase=OpenProvider; status=Pass; category=None; property=None; HRESULT=none
phase=CreateUnnamedKey; status=Pass; category=None; property=None; HRESULT=none
phase=Construction; status=Pass; category=NullNameUserFlags0ConstructionOnlyNotDiskProof; property=None; HRESULT=none
phase=SetExportPolicy; status=Pass; category=Requested3SetReturnedSuccessNotReadback; property=None; HRESULT=none
phase=SetSigningUsage; status=Pass; category=SigningOnly2SetReturnedSuccess; property=None; HRESULT=none
phase=Finalize; status=Pass; category=None; property=None; HRESULT=none
phase=Metadata; status=Pass; category=RetainedReadbacksOnlyNoUiNameOrPolicyAssessment; property=None; HRESULT=none
phase=Sizing; status=Pass; category=None; property=None; HRESULT=none
phase=Fill; status=Pass; category=None; property=None; HRESULT=none
phase=Structure; status=Pass; category=EphemeralExportFrameObservedNotPolicyEnforcement; property=None; HRESULT=none
phase=Zeroization; status=Pass; category=None; property=None; HRESULT=none
phase=ReleaseKey; status=Pass; category=None; property=None; HRESULT=0x00000000
phase=ReleaseProvider; status=Pass; category=None; property=None; HRESULT=0x00000000
zeroization=ClearedFullCapacity; provider-UI-possible=true; no technical no-dialog guarantee; unexpected UI invalidates run; POLICY PROOF=NO-GO; STOP for owner review
```
