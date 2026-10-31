# A2.2C5 — ephemeral metadata failure investigation (code only)

## Conclusion / scope

Microsoft explicitly limits **UI Policy to persistent keys**. Requesting it on this
unnamed ephemeral key is a strong unsupported-property hypothesis, not a confirmed
root cause: the owner's old trace reports only Metadata/NTE_NOT_SUPPORTED. Earlier
reads can also fail. Export Policy documentation additionally describes persisted
key policy; that ambiguity must not be papered over. No native attempt was repeated.

Correction is diagnostics only, plus retirement of the used operation. All metadata
requirements, UI SET/GET, full structure validation, exact provider/P256/policy3,
signing-only/NULL-name/user scope, export flags0/blob/104-byte bound, zeroization,
consent and cleanup remain intact. No property is skipped or silently marked valid/
not-applicable. Unresolved security consequences mean **NO gate-removal implemented**.
Stop for separate owner design review; no execution command or request is supplied.

Reviewed base `9e0a41f1fbe65c60b857e9d663aa21d947038a8f`, existing feature branch
`feature/stage-a2-2c5-isolated-security-harness`, draft PR #13 stacked on #12.
AGENTS.md, actual native helpers/model/engine/Main/tests, strategy and static audit
reviewed. No unrelated work, Android, production source or original fixture modified.

## Owner evidence — not cloud/native proof

Approved attempt `3bb5de0629d640569843367dd3407fc8` reached Consent/OwnerContext/
OpenProvider/CreateUnnamedKey/Configure/Finalize PASS, Metadata INCONCLUSIVE with
0x80090029. Sizing/Fill/Structure/Zeroization NOT RUN; no private output allocated.
Key/provider release PASS0 was harness-reported, not independently verified.
CAPABILITY Inconclusive, POLICY NO-GO. ID now refused case-insensitively before API
access. Earlier retired `3c4a7c190ef34f2e90625276aa9cbfdb` and its journal remain
untouched and blocked. No replacement ID generated or run.

## Property-by-property evidence matrix

Call sites are `WindowsCapabilityApi.Key.ReadMetadata`, final source lines62–73.
Order is retained: provider Name; algorithm name/group (group short-circuits when
name mismatches); UI Policy; key Name; Length; Export Policy; Key Type; Key Usage.
Every existing requirement remains mandatory. On unsupported read, report its fixed
identifier and native status, stop INCONCLUSIVE, release owned handles, no export.
Other unexpected native errors/malformed values fail; no fallback/retry.

| Diagnostic ID / wire property | Handle / current call | Microsoft applicability | Needed for this proof / unresolved point |
| --- | --- | --- | --- |
| ProviderName / Name | Provider, line62 | Name of object; no explicit ephemeral guarantee or provider-specific value promise in generic row | Exact software provider evidence retained; unsupported cannot be assumed matching |
| KeyAlgorithmName / Algorithm Name | Key, line63 | Applies only to keys, not limited to persisted keys | P256 algorithm mandatory; provider behavior still must be observed |
| KeyAlgorithmGroup / Algorithm Group | Key, line64 | Applies only to keys; Microsoft KSP lists ECDSA | ECDSA group mandatory; same unsupported rule |
| KeyUiPolicy / UI Policy | Key, line65; SET in Configure | Explicitly **only persistent keys**; set during generation, read-only after finalize | Current approved no-UI metadata gate retained; using persisted-only property for ephemeral control is a design mismatch requiring review |
| KeyName / Name | Key, line70 | Object name; no universal ephemeral Boolean or guarantee of successful empty-name read | NULL-name creation is documented ephemeral; empty-name read is extra current corroboration, never inferred from unsupported/not-found |
| KeyLength / Length | Key, line71 | Bit length, applies only to keys | Actual256 mandatory; malformed/overflow values fail with property tag |
| KeyExportPolicy / Export Policy | Key, line72 | DWORD describes export policy **for a persisted key**, then says applies only to keys | Actual policy3 mandatory; wording is narrower/ambiguous for ephemeral readback, not a proven guarantee or automatic permission to drop it |
| KeyType / Key Type | Key, line72 | Key flags; absent machine flag means current user; applies only to keys | Actual type0 mandatory; not proof of ACL/cross-user isolation |
| KeyUsage / Key Usage | Key, line73 | Key usage flags; applies only to keys | Actual signing-only2 mandatory |

Documentation facts do not establish that every key-general property is implemented
on every KSP/OS/key lifecycle. The UI row explicitly excludes ephemeral applicability;
Export Policy wording warrants a separate provider-specific review, not a claim that
it definitely fails on ephemeral keys. No private/unique key name or inventory is
requested or logged. Name is read locally for equality/emptiness only.

## SET success is not GET support / unchanged behavior decision

Configure PASS means these existing SET calls returned success, including UI Policy
before finalization. It does **not** prove that the property is supported for readback
on an ephemeral finalized object, that settings take effect as intended, or that UI
is impossible. NCryptGetProperty documents NTE_NOT_SUPPORTED as the specified
property not supported for this object; neither UI-policy causality nor export-policy
causality follows from the generic old phase error.

Options for separate owner design review:

- Retain current fail-closed gate and diagnostic-only code. This is implemented; no
  new run is requested and existing failure remains inconclusive.
- A future ephemeral-specific contract could explicitly call persisted-only UI
  property NotApplicable and avoid both SET and GET. That would remove the present
  NoUiPolicy readback requirement; it is a security/acceptance change, not merely an
  error fix. It needs explicit owner approval and an honest UI-risk policy.
- Silently setting NoUiPolicy=true on unsupported, ignoring only GET after SET, using
  undocumented property success or removing other metadata to reach export is not
  defensible. No such change made.
- Flags0 cannot guarantee no UI. NCRYPT_SILENT_FLAG, watchdog/child process or a new
  cancellation model would change the approved experiment and require separate review.
  No guard/flags switch, UI interaction or retry added. The control may remain blocked
  even after UI reconsideration because other required metadata can be unavailable.

The next owner decision is whether to commission a separate design for an ephemeral-
appropriate metadata/no-UI contract, including Export Policy and unnamed corroboration.
This stage does not ask for authorization to execute or collect new native evidence.
No current change makes unsupported metadata successful or not-applicable.

## Minimal diagnostics / parsing corrections

New MetadataDiagnostics.cs defines nine allowlisted property IDs, handle kind and
documentary applicability, with exact wire names. Classification is informational,
not a runtime bypass. Native ReadProperty accepts an enum rather than arbitrary
property strings. Read failures carry only fixed property/error kind and returned
HRESULT; no values, exception messages, SID, pointers, names/paths or private data.
ProviderName and KeyName are distinct diagnostic IDs even though both wire names
are Name. Invalid property labels become UnknownMetadataProperty, never arbitrary text.

Native read bound remains512 bytes, no size retry. ReadText/Dword parsing and bit-
length validation retain property context on malformed data. Full UI structure still
required. Managed exceptions at the native read boundary are sanitized without
retaining the raw exception. Engine preserves Metadata phase, existing HRESULT
classification, INCONCLUSIVE/FAIL distinction, Memory NotAllocated and cleanup.
Main prints property=<allowlisted ID> or None alongside phase/category/HRESULT.
No success or value is fabricated if a property was not read.

Tests inject each of nine unsupported property outcomes and verify no size/fill,
no invented zeroization, both owned resources closed once, policy NO-GO. Malformed/
unexpected reads retain safe property and fail; parsing/labels/classification and
retired-ID rejection are pure. Existing UI/persistence mismatch assertions preserved.
These tests prove reporting/decision logic only, not native property availability.

## Exact changed files

- tools/PeripheralBatteryCompanion.EphemeralCapability/MetadataDiagnostics.cs (new)
- tools/PeripheralBatteryCompanion.EphemeralCapability/WindowsCapabilityApi.cs
- tools/PeripheralBatteryCompanion.EphemeralCapability/CapabilityModel.cs
- tools/PeripheralBatteryCompanion.EphemeralCapability/CapabilityEngine.cs
- tools/PeripheralBatteryCompanion.EphemeralCapability/Program.cs
- tests/PeripheralBatteryCompanion.Tests/PeripheralBatteryCompanion.Tests.csproj
- tests/PeripheralBatteryCompanion.Tests/EphemeralCapabilityTests.cs (added cases only)
- tests/PeripheralBatteryCompanion.Tests/EphemeralMetadataDiagnosticsTests.cs (new)
- docs/STAGE_A2_2C5_METADATA_FAILURE_INVESTIGATION.md (this report)
- docs/STAGE_A2_2C5_EPHEMERAL_CAPABILITY_IMPLEMENTATION.md (latest investigation notice)

## Verification / risks / STOP

Linux/.NET SDK10.0.403. Locked restore PASS; Release solution build PASS,0 warnings/
errors; portable suite **701 passed,0 failed,0 skipped** (687 prior +14 new pure
cases). Existing assertions preserved. Native adapter/tool/Windows tests compiled
only; preview, CNG, original fixture and owner experiments NOT RUN. No property read,
key generation/export/deletion, certificate, journal access, private buffer or native
zeroization/independent cleanup observation in this task. Logs/TRX retained at
`/workspace/stage-a2-2c5-metadata-artifacts/`.

Remaining root cause is unconfirmed; no property-specific owner trace exists from
the one approved attempt. Diagnostics are ready for review, not a request to rerun.
The current ephemeral UI contract remains unresolved and may be unsupported by design.
Provider/runtime/paging/UI/interop risks from earlier reports remain. No production
activation, persisted policy proof, DPAPI/TLS/pairing, merge/rebase/release authorized.

## Microsoft references consulted

- [Key-storage property identifiers](https://learn.microsoft.com/en-us/windows/win32/seccng/key-storage-property-identifiers): all nine property applicability rows, especially UI Policy and Export Policy wording.
- [NCryptGetProperty](https://learn.microsoft.com/en-us/windows/win32/api/ncrypt/nf-ncrypt-ncryptgetproperty): object support, output bound, NTE_NOT_SUPPORTED meaning.
- [NCRYPT_UI_POLICY](https://learn.microsoft.com/en-us/windows/win32/api/ncrypt/ns-ncrypt-ncrypt_ui_policy): version/flags/pointers; does not promise no UI for flags0.
- [NCryptCreatePersistedKey](https://learn.microsoft.com/en-us/windows/win32/api/ncrypt/nf-ncrypt-ncryptcreatepersistedkey): NULL key name gives ephemeral creation; no inference from unsupported Name getter.

**NATIVE EXPERIMENT NOT RUN; OWNER AUTHORIZATION REQUIRED FOR ANY FUTURE RUN; POLICY PROOF REMAINS NO-GO.**
