# A2.2C5 — focused native interop static audit

**NATIVE CAPABILITY EXPERIMENT NOT RUN; OWNER AUTHORIZATION REQUIRED. POLICY PROOF REMAINS NO-GO.**

## Scope, baseline and result

Reviewed `de5273c644df955d202dbf6433159e2eb7933d7a`, branch
`feature/stage-a2-2c5-isolated-security-harness`, draft PR #13 stacked on #12.
Reviewed actual adapter/model/engine/Main, tests, implementation/strategy reports,
solution wiring and AGENTS.md. No pre-existing work or unrelated untracked files
modified. No preview, capability or original fixture invocation, journal access,
native security operation or owner-machine cleanup occurred.

Two concrete corrections: reject truncated UI-policy readback; prevent managed
exceptions escaping SafeHandle release. Added direct managed fake-handle tests and
pure acquisition decisions. No HRESULT acceptance, export provider/blob/flags/policy,
consent or original fixture behavior changed. This audit is not native validation.

## Findings / file:line evidence

1. **Malformed UI metadata accepted (correctness/fail-closed).** Baseline
   `WindowsCapabilityApi.cs:68`–70 accepted only eight bytes (version/flags), but
   NCRYPT_UI_POLICY also contains three native-width pointers: 20 bytes x86, 32 bytes
   x64/ARM64 with standard Windows layout. A truncated successful result could pass
   NoUiPolicy and reach export without a complete structure. Final
   `InteropContracts.cs:22`–29 requires full layout and version1; adapter
   `WindowsCapabilityApi.cs:68`–70 rejects malformed data before export. Nonzero
   UI flags still cannot pass; flags0 remain no absolute promise of no dialog.
2. **ReleaseHandle could throw (cleanup contract).** Baseline
   `WindowsCapabilityApi.cs:104`–108 called the P/Invoke without an exception boundary.
   A managed interop/loader exception could escape cleanup/finalization, violating
   SafeHandle's no-throw release contract. Final `InteropContracts.cs:39`–49 catches
   exceptions without messages, reports ReleaseSucceeded=false/HRESULT unknown and
   returns false. Native subclass `WindowsCapabilityApi.cs:95`–99 supplies only the
   NCryptFreeObject call. Nonzero native status is preserved; no retry/double-free.
   This is a static issue, not a claim such an exception occurred on owner's PC.

References to baseline lines use reviewed commit above; final lines refer to this
change's source. No exploitable external-input or observed native crash is claimed.

## P/Invoke declaration audit

Final `WindowsCapabilityApi.cs:139`–153 compared with Microsoft signatures:

| Function | Declaration / contract checked |
| --- | --- |
| NCryptOpenStorageProvider | out owning native-width SafeHandle, Unicode provider name, DWORD flags0 |
| NCryptCreatePersistedKey | provider SafeHandle; out key; Unicode algorithm; nullable name=NULL; DWORD legacySpec0/flags0 |
| NCryptSetProperty | object SafeHandle; Unicode property; caller pointer, 32-bit size and flags |
| NCryptGetProperty | object SafeHandle; Unicode property; bounded caller pointer/capacity, out 32-bit length, flags0 |
| NCryptFinalizeKey | key SafeHandle, 32-bit flags0 |
| NCryptExportKey | key SafeHandle, NULL optional wrapping handle, Unicode ECCPRIVATEBLOB, NULL parameter list, NULL sizing or pinned fill pointer, capacity/out length, flags0 |
| NCryptFreeObject | raw native-width value inside owning SafeHandle release; SECURITY_STATUS return |

All return types are 32-bit signed SECURITY_STATUS. DWORD parameters represented by
32-bit int retain ABI width; fixed flags are0, capacities small, negative/overflowed
reported lengths are rejected. No GetLastError inference; PreserveSig default keeps
returned status. DllImport calling convention defaults to Winapi (stdcall on Win32,
platform ABI on Win64). String declarations use Unicode/ExactSpelling. Finalize/Free
have literal entrypoints and no strings; default lookup resolves the documented exact
exports. No calling-convention/width/entrypoint correction was needed. No runtime
Windows marshalling evidence is inferred from these checks.

Blob constant ECCPRIVATEBLOB is BCRYPT_ECCPRIVATE_BLOB. Header magic ECS2=
0x32534345, cbKey=32; header8+X32+Y32+d32=104. Header DWORDs use little-endian Windows
layout; coordinate/scalar payload is big-endian but never parsed/copied/logged.
Representation validation is not mathematical key validity or policy proof.

## Handles, lifetime, acquisition and concurrency

- SafeHandle arguments hold a call-scoped runtime reference during P/Invoke.
- Key wrapper retains provider handle (`WindowsCapabilityApi.cs:41`), and synchronous
  engine owns provider until its finally block closes key, then provider
  (`CapabilityEngine.cs:101`–102). Thus ordinary current engine execution keeps
  provider reachable/undisposed throughout key work and release; no async/native
  background work, sharing or concurrent disposal is introduced.
- Retaining a managed handle reference is NOT a long-lived DangerousAddRef lease
  against another thread explicitly calling Dispose. Wrappers/engine are not a
  general thread-safe concurrent API. An independently disposed provider would cause
  guarded calls to fail, not be evidence of native success. Any future concurrency
  or resource sharing requires separate lifetime design; no speculative lease added.
- New pure `InteropContracts.Acquisition` is used by existing CheckCreated: only
  status0 AND valid fresh output is accepted. Success/invalid output fails; nonzero
  status never becomes acceptance even with a valid output. Error/valid output is
  released once before rejection and carries explicit partial-acquisition evidence;
  error/invalid output invokes no native release. No existing named key is opened or
  adopted. The unusual error-with-output path remains a trusted native API assumption,
  not proof from sentinel checking that arbitrary values are valid OS resources.
- Microsoft guarantees ReleaseHandle once for valid owned handles; invalid0/-1 never
  invokes it. SafeHandle may defer release while a call reference exists, so Dispose
  alone does not prove native release. Engine requires actual ReleaseSucceeded=true;
  null/deferred, false, thrown or failed partial release cannot yield capability PASS.
- NCryptFreeObject failure is NOT documented as a retry-safe operation. SafeHandle
  becoming closed does not prove OS resource freed when ReleaseHandle returned false.
  No second free, orphan adoption or automatic cleanup; preserve unconfirmed status
  and STOP. Engine still attempts the separately owned provider disposal, performs no
  further key use, and makes no successful-cleanup claim. Process teardown is not
  claimed as independently verified cleanup/zeroization.

## Zeroization / exception audit

`CapabilityEngine.cs:54`–61 requires sizing104 before allocation, fill104 and valid
header. Adapter passes pinned pointer directly, not a private array marshaler.
`CapabilityEngine.cs:77`–102 clears full allocated output in finally before either
resource release, including partial-write errors, changed sizes, malformed structure,
managed body exceptions and cleanup failures. No resize/copy/private serialization,
PFX/disk/console exposure. Invalid sizing allocates nothing. New handle correction
does not change any private-buffer path; all existing48 capability tests preserved.

Normal managed unwinding only: no promise for provider/OS/runtime internal copies,
paging/dumps, native memory corruption, fatal resource exhaustion, external process
kill or power loss. Allocations/reporting during finally also prevent claiming
infallible recovery under arbitrary out-of-memory conditions. Zeroization code and
managed test observations are not native provider memory proof.

## UI policy, gates and status review

NativeUiPolicy Sequential layout is DWORD version1, DWORD flags0, three NULL LPCWSTR
members; offsets0/4/8/8+pointer/8+2*pointer; sizeof20 or32. Same shared structure is
used for setting and pure layout tests. Full versioned readback now required; trailing
public string storage is allowed, never dereferenced or logged.

No flags substitution, alternate provider/blob or retry. Flags0 cannot guarantee no
provider UI, and NULL title/name fields permit provider defaults if UI occurs.
UI Policy flags0 requests no strong UI; it is not a technical no-dialog guarantee.
Unexpected provider dialog requires STOP without interaction. Nonzero UI metadata or
NTE_SILENT_CONTEXT remains INCONCLUSIVE; no prompt acknowledgement logic. A synchronous
call may block, and external termination may bypass finally. NCRYPT_SILENT_FLAG
changes the experiment's fixed semantics; a UI watchdog/child process changes context,
cancellation and cleanup assumptions. Neither is introduced or authorized by this
static audit. Future execution approval must explicitly consider the remaining risk.

Main checks exact consent/operation/platform before API access; engine repeats consent,
then ordinary owner context. Preview formatting has no native provider calls and is
not invoked here. v4/variant checks and retired-ID rejection retained. Freshness and
human approval are workflow requirements, not cryptographically proven by an ID.
No test/CI/startup auto-run; no network, persistent names, certificates/stores, journal,
DPAPI/TLS/pairing, Android or production identity. Current tool has no DeleteKey/OpenKey
or filesystem APIs. Original persisted fixture and retained owner journal untouched.

PASS is ephemeral export capability only; FAIL, INCONCLUSIVE and NOT RUN remain
separate. Unsupported results never become PASS, no added accepted HRESULTs. Policy
proof is always NO-GO. Release evidence is independent of export success. No new
permission or native command follows from the audit or portable test results.

## Exact change inventory / verification

1. `tools/PeripheralBatteryCompanion.EphemeralCapability/WindowsCapabilityApi.cs`:
   full UI validation/shared layout; safe release subclass; pure acquisition guard.
2. New `tools/PeripheralBatteryCompanion.EphemeralCapability/InteropContracts.cs`:
   shared layout/validation, acquisition decisions, no-throw checked SafeHandle base.
3. `tests/PeripheralBatteryCompanion.Tests/PeripheralBatteryCompanion.Tests.csproj`:
   links new portable contract file only; native adapter remains excluded.
4. New `tests/PeripheralBatteryCompanion.Tests/EphemeralInteropAuditTests.cs`:
   13 pure cases, synthetic managed handles, zero Windows/crypto/OS release calls.
5. This audit document.
6. Implementation report: latest-audit note, historical counts preserved.

Linux, .NET SDK10.0.403. Locked restore PASS; Release solution build PASS,0 warnings/
errors; portable tests **687 passed,0 failed,0 skipped** (674 prior +13 new). Tests:
UI truncation rejection under both modeled pointer widths; host struct offsets;
success/error x valid/invalid acquisition truth table; release success/nonzero/throw
without retry; invalid-handle no-release; SafeHandle reference deferral. Host managed
layout is not Windows x86/ARM64 ABI execution; actual Windows interop NOT RUN.
Native adapter, Main, preview, CNG generation/export/free, original fixture and owner
machine operations all NOT RUN. No native key/certificate/resource created/deleted.
Logs/TRX: `/workspace/stage-a2-2c5-native-audit-artifacts/`.

## Microsoft references

- [NCryptOpenStorageProvider](https://learn.microsoft.com/en-us/windows/win32/api/ncrypt/nf-ncrypt-ncryptopenstorageprovider)
- [NCryptCreatePersistedKey](https://learn.microsoft.com/en-us/windows/win32/api/ncrypt/nf-ncrypt-ncryptcreatepersistedkey)
- [NCryptSetProperty](https://learn.microsoft.com/en-us/windows/win32/api/ncrypt/nf-ncrypt-ncryptsetproperty)
- [NCryptGetProperty](https://learn.microsoft.com/en-us/windows/win32/api/ncrypt/nf-ncrypt-ncryptgetproperty)
- [NCryptFinalizeKey](https://learn.microsoft.com/en-us/windows/win32/api/ncrypt/nf-ncrypt-ncryptfinalizekey)
- [NCryptExportKey](https://learn.microsoft.com/en-us/windows/win32/api/ncrypt/nf-ncrypt-ncryptexportkey)
- [NCryptFreeObject](https://learn.microsoft.com/en-us/windows/win32/api/ncrypt/nf-ncrypt-ncryptfreeobject)
- [NCRYPT_UI_POLICY](https://learn.microsoft.com/en-us/windows/win32/api/ncrypt/ns-ncrypt-ncrypt_ui_policy)
- [BCRYPT_ECCKEY_BLOB](https://learn.microsoft.com/en-us/windows/win32/api/bcrypt/ns-bcrypt-bcrypt_ecckey_blob)
- [SafeHandle.ReleaseHandle contract](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.safehandle.releasehandle?view=net-10.0)

STOP for owner code review. No native execution command supplied, no implied execution
authorization, no merge/rebase/release. Native ABI/provider/metadata/UI/cleanup behavior
remains unverified; policy proof and production activation remain NO-GO.
