# A2.2C5 — ephemeral capability implementation / security review

**NATIVE CAPABILITY EXPERIMENT NOT RUN; OWNER AUTHORIZATION REQUIRED. POLICY PROOF REMAINS NO-GO.**

## Baseline / authorization boundary

Base `25d1f0000b6581322c16cfc9880915900524e6ee`, existing branch
`feature/stage-a2-2c5-isolated-security-harness`, draft PR #13 stacked on #12.
AGENTS.md, reviewed export strategy and actual existing harness/guards inspected.
Owner authorizes implementation, compilation and pure tests only; chooses export
flags **0** for comparability. No native invocation, preview execution, owner-machine
resource access, journal operation or fixture rerun performed. No execution command
is provided in this delivery. Existing fixture behavior/source is unchanged.

## Exact changed-file inventory

- Solution: adds separate `tools/PeripheralBatteryCompanion.EphemeralCapability`
  project to compile, not execute. No platform configuration change.
- New project's csproj and packages.lock.json: net10.0-windows console, unsafe limited
  to native pointer interop, zero NuGet dependencies.
- CapabilityModel.cs: consent/preview, fixed evidence enums and narrow native resource
  interfaces; no Windows implementation included in portable test source links.
- CapabilityEngine.cs: fixed lifecycle, bounded export/structure validation, full
  buffer clearing and independent disposal/error evidence.
- WindowsCapabilityApi.cs: Windows-only NCrypt P/Invoke adapter and owning SafeHandles.
- Program.cs: separate console entrypoint; pure preview formatting or exact future
  execution gate. No hook in tray, old fixture, test.ps1, UI, CI or app startup.
- Portable test csproj: links only new pure model/engine files, not entrypoint/adapter.
- EphemeralCapabilityTests.cs: synthetic fake-API/failure-injection cases.
- This review. No other runtime source, dependencies or existing test assertions
  modified; Android, snapshot-v1, battery/G HUB/HID/tray behavior and exports untouched.

## Consent, scope and non-persistence

The preview formatter accepts only a valid nonzero v4/variant GUID; CLI preview
creates a random v4 GUID and writes only stdout. It does not query identity, load a
provider, open a key/store, build, write files or mutate machine configuration. Preview
entrypoint itself was NOT RUN; only formatting/validation exercised in pure tests.
Future execution needs separate exact confirmation and approved fresh preview ID;
invalid/wrong/retired IDs stop before even owner-context lookup. The retired ID
`3c4a7c190ef34f2e90625276aa9cbfdb` is blocked case-insensitively. No access to its key
or journal; no recovery/adoption/deletion logic exists in this tool.

Freshness/approval are a human workflow gate, not a cryptographic approval proof or
durable replay database. No journal is allowed here, so the tool cannot prove an ID
was displayed previously or unused in another process. Future owner must use a newly
preview-generated ID and approve its scope; no automatic retries or ordinary-test
execution. Program dispatches once, with no loop. No command is supplied now.

Native execution, if separately approved later, requires an interactive authenticated
ordinary current user; anonymous/guest/SYSTEM/elevated administrator contexts refuse.
No elevation or privileges/account changes. Exactly one unnamed key is created via
NCryptCreatePersistedKey with NULL name, ECDSA_P256, legacySpec=0, creationFlags=0.
Microsoft documents this combination as ephemeral, not a persisted container. No
NCryptOpenKey, NCryptDeleteKey, certificate/store, filesystem, registry, DPAPI, TLS,
network/profile/firewall/router, pairing or production identity API exists here.
Only NCryptFreeObject disposes owned handles; never prefix or orphan cleanup.

## Fixed native sequence and signatures

1. NCryptOpenStorageProvider(out owning provider, Microsoft Software KSP, flags=0).
2. NCryptCreatePersistedKey(provider, out owning key, ECDSA_P256, NULL, 0, 0).
3. NCryptSetProperty: Export Policy DWORD=3 (AllowExport|AllowPlaintextExport), Key
   Usage DWORD=2 (signing only), UI Policy version=1/flags=0/null strings. All before
   finalization, no archiving or persistent-property flags. Any unsupported call stops.
4. NCryptFinalizeKey(key, flags=0), exactly once; no NO_KEY_VALIDATION, legacy-store,
   machine or overwrite flag, no finalization retry.
5. Bounded NCryptGetProperty actual provider Name, Algorithm Name/Group, Length,
   Export Policy, Key Type, Name, UI Policy, Key Usage. Fixed public-metadata buffer
   (512 bytes); malformed/oversize/failure stops, no size-retry or broad enumeration.
6. NCryptExportKey(key, wrappingKey=NULL, blobType=ECCPRIVATEBLOB,
   parameterList=NULL, output=NULL, capacity=0, out length, flags=0): one sizing call.
7. Only exact expected length permits allocation and one fill call with same arguments,
   pinned caller-owned pointer/capacity=104; no byte-array marshaler/hidden managed
   private output array, wrapping key, format/flags alternative, resize, copy or retry.
8. Zero caller output, dispose key then provider; report each status independently.

P/Invoke uses Unicode exact entrypoints, SECURITY_STATUS represented by 32-bit int,
handles/pointers as native-width SafeHandle/IntPtr, DWORD widths as 32-bit values.
Outputs are passed by pointer; no GetLastError inference (use returned status).
Creation failures dispose any partially returned valid handle before throwing and
carry explicit partial-acquisition release evidence back to the engine. Release
HRESULT/status is reported; no double-close or invented NOT RUN release. Failed
release evidence is fail-closed. Native SafeHandle finalization is emergency lifetime
support; success reporting requires deterministic Dispose/ReleaseHandle success.
Pure tests verify interface ownership/order; native handle marshalling/release behavior
itself is NOT RUN. No native DeleteKey is used even on failure.

## Actual metadata and ephemeral evidence limits

A PASS requires provider Name equals Microsoft Software Key Storage Provider,
Algorithm Name=ECDSA_P256, Algorithm Group=ECDSA, Length=256, Export Policy=3,
Key Type=0 (no machine scope), successful empty key Name, UI Policy version1/flags0
and Key Usage=2. Output reports fixed categories, never actual names/SIDs/paths.
Mismatch stops before export, with a distinct sanitized category. Nonzero UI policy
is INCONCLUSIVE; forbidden persistence is FAIL; no replacement key is generated.

There is no documented universal native IsEphemeral BOOL. Evidence combines NULL-name
creation semantics with actual successful empty Name readback. Do not use .NET's
caller-set ephemeral marker or treat NTE_NOT_FOUND/NOT_SUPPORTED as an empty name.
If this OS/KSP does not provide the required readback, stop with unavailable evidence;
do not silently weaken it. This may prevent reaching export; it is a known native
compatibility uncertainty, not permission to infer success. Provider metadata comes
from the same explicit owning provider used to create the key; no provider fallback.

## Memory ownership / buffer rationale

```text
engine -> provider SafeHandle -> one unnamed key SafeHandle
                     key sizing -> exact 104 -> engine byte[104]
                                                | pinned pointer (no private copy)
                                                v
                                     one NCryptExportKey fill
                                                |
                                     structural header checks
                                                |
                           finally: clear ALL 104; check cleared
                                                |
                                   release key, then provider
```

BCRYPT_ECCKEY_BLOB header has two DWORDs (8 bytes); P-256 private blob appends
X[32], Y[32], d[32], totaling **104**. Require ECDSA_PRIVATE_P256 magic 0x32534345
(ECS2), cbKey=32, exact sizing and fill lengths=104. Reject negative/zero/oversize/
changed lengths before parsing; do not resize to accommodate them. Structure checks
validate representation, not mathematical key authenticity, private scalar quality
or target security. Native trusted provider produces the key; no private import or
copy is performed for verification. Public metadata copying is separate and contains
no exported private material; private output never enters serialization or logs.

Output belongs to engine. CryptographicOperations.ZeroMemory clears its **entire
allocated capacity** in finally after success, partial native error, invalid fill
length, bad structure or managed exception; a synthetic wrong-capacity allocator is
also cleared without a native fill. Invalid sizing causes no allocation. A synthetic
retained-array test observes zero after return; evidence records clearing before
resource release. Provider/key cleanup is attempted even if the other disposal fails.
Private output remains in memory only, never PFX/disk/dump/console. No automated dump
collection is requested or implemented.

These guarantees cover normal managed unwinding and buffers owned by this code, not
provider/runtime/OS internals, paging, crash dumps, memory corruption, process kill,
power loss or privileged inspection. Native interop may fail catastrophically outside
recoverable exception paths. Never claim absolute erasure or hardware protection.

## Flags=0 and UI / interruption limitations

Flags=0 is fixed by owner decision and matches the reviewed managed export path's
flags. Explicit UI Policy flags0 and software provider are checked; NTE_SILENT_CONTEXT
(0x80090022) or UI-required metadata stops as INCONCLUSIVE, no prompt acknowledgement,
retry or switch to silent flags. There is no NCRYPT_SILENT_FLAG substitution.

Flags0 cannot guarantee a provider never opens UI or blocks before returning. Code
cannot reliably detect/cancel an unexpected external provider dialog while inside
that synchronous call. Future execution requires owner supervision: do not interact
with unexpected UI; stop and review. External termination may bypass finally, so
absence of a completion report is interrupted/INCONCLUSIVE, not zeroization/disposal
PASS. No hard timeout or UI watcher is claimed. This limitation is explicitly part
of the scope requiring later owner approval; native invocation remains unauthorized.
An unexpected persistent artifact requires STOP, never automatic scan/adoption/delete.

## Status/decision and safe reporting

| Condition | Capability result | Policy proof |
| --- | --- | --- |
| No exact consent/platform approval or no native invocation | NOT RUN | NO-GO |
| All metadata, fixed size/fill, structure, zeroization and owned releases succeed | PASS (ephemeral support only) | NO-GO |
| Unsupported sizing/fill or UI-required/silent-context result | INCONCLUSIVE | NO-GO |
| Other errors, metadata mismatch, unexpected output, wrong lengths/structure or disposal failure | FAIL | NO-GO |

Sizing and fill are separate phases. Unreached phases print NotRun. Resource releases
not acquired never print invented success. Memory says NotAllocated versus
ClearedFullCapacity; failed cleanup cannot be overridden by successful export.
Successful private export is EXPECTED for this deliberately exportable key and is
not a target export-policy test. No automatic attachment to old fixture criteria.

Evidence contains fixed phase/outcome/category and numeric returned HRESULT only;
unknown managed exceptions use UnexpectedException without message/type/name/path.
Metadata uses enumerated categories; no bytes, coordinates/scalar, SID, certificate
inventory, key/container name or user path. Build tools may emit local paths; share
only sanitized capability output if separately authorized later. No diagnostic file
is written by this tool. Capability result always exposes policy proof NO-GO.

## Verification / tests

Linux .NET SDK 10.0.403. New dependency-free project lock generated, then locked solution
restore PASS; Release build PASS, 0 warnings/errors. Portable suite **674 passed,
0 failed, 0 skipped** (previous626 +48 new pure failure-injection cases). All existing
assertions preserved. Windows native tool and existing test assembly compiled only.
No capability entrypoint/preview/script or native security test executed.

New synthetic tests cover: consent/retired ID/context gates; success with no policy
promotion; unsupported sizing and fill after partial write; negative/oversize/changed
lengths; bad magic/coordinate-width structure; every metadata mismatch including
persistence and UI; silent-context/state/access errors; no fallback/retry; create/
configure/finalize/metadata failures; pre-acquisition failure; zero full allocation on
partial error/bad output/allocator size; exact disposal once/order, cleanup false and
throwing cleanup; partial provider/key acquisition with successful/failed release;
output redaction. Fakes write synthetic patterns, never keys.
Test assembly links model/engine only, not Windows adapter or Main. Fake PASS is
software-logic evidence, never native provider/interop/ephemeral-release proof.
Logs/TRX retained in `/workspace/stage-a2-2c5-ephemeral-artifacts/`.

## Microsoft source references

- [NCryptOpenStorageProvider](https://learn.microsoft.com/en-us/windows/win32/api/ncrypt/nf-ncrypt-ncryptopenstorageprovider)
- [NCryptCreatePersistedKey (NULL name = ephemeral)](https://learn.microsoft.com/en-us/windows/win32/api/ncrypt/nf-ncrypt-ncryptcreatepersistedkey)
- [NCryptSetProperty](https://learn.microsoft.com/en-us/windows/win32/api/ncrypt/nf-ncrypt-ncryptsetproperty)
- [NCryptFinalizeKey](https://learn.microsoft.com/en-us/windows/win32/api/ncrypt/nf-ncrypt-ncryptfinalizekey)
- [NCryptGetProperty](https://learn.microsoft.com/en-us/windows/win32/api/ncrypt/nf-ncrypt-ncryptgetproperty)
- [Property identifiers / export/UI/name policy](https://learn.microsoft.com/en-us/windows/win32/seccng/key-storage-property-identifiers)
- [NCryptExportKey](https://learn.microsoft.com/en-us/windows/win32/api/ncrypt/nf-ncrypt-ncryptexportkey)
- [BCRYPT_ECCKEY_BLOB structure](https://learn.microsoft.com/en-us/windows/win32/api/bcrypt/ns-bcrypt-bcrypt_ecckey_blob)
- [NCryptFreeObject](https://learn.microsoft.com/en-us/windows/win32/api/ncrypt/nf-ncrypt-ncryptfreeobject)
- [HRESULT definitions](https://learn.microsoft.com/en-us/windows/win32/com/com-error-codes-4)

## STOP / owner review

Review code, native signatures, fixed metadata requirements, flags0/UI limitations,
zeroization and release evidence before any later experiment. No preview/execution
command or invitation is supplied. Future execution needs a separate explicit owner
approval of reviewed scope and exact fresh preview-generated operation ID. Retired
journal/resource stays untouched. No production provisioning, target mutation,
DPAPI, TLS/phone/Android/hardware work, merge, rebase or release is authorized.

**NATIVE CAPABILITY EXPERIMENT NOT RUN; OWNER AUTHORIZATION REQUIRED. POLICY PROOF REMAINS NO-GO.**
