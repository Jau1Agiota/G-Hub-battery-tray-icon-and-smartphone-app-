# W1 metadata and deterministic host gates — design only

No runnable implementation/configuration, restore, build, discovery or execution.
[Findings/provenance](W1_GATE_P_BLOCKER_RESOLUTION.txt) ·
[Owner choices](W1_GATE_P_OWNER_DECISIONS.md).

## P2: presence-aware metadata readback

**Source inference**, not provider proof. VMR build commit
`4099f83977f4d29604a9aeb971fb0a7bc1d5f699` maps runtime source to
`6aa72daa24f66c01c3d7b6a32519e9d21396deec`; relevant files match bytewise.
Use only public `CngKey.GetProperty(name, options)` and `CngProperty.GetValue()`
after a separately approved single key exists inside Gate E. No call is made now.
No `SetProperty` repair, PInvoke/reflection, raw handles or export probe.

Exact source references, relative to runtime commit above:

- `src/libraries/System.Security.Cryptography/src/System/Security/Cryptography/CngKey.Properties.cs:18–46`:
  GetProperty throws NTE_NOT_FOUND for missing; zero bytes become null; HasProperty
  returns false only for NTE_NOT_FOUND and throws on other failures.
- `src/libraries/Common/src/System/Security/Cryptography/CngHelpers.cs:61–113`:
  size query + allocation + fetch/resize; missing at either call becomes null and
  is rethrown by GetProperty. No application retry around its internal calls.
- `src/libraries/System.Security.Cryptography/src/System/Security/Cryptography/CngProperty.cs:19–27,50`:
  constructor/GetValue clone bytes; value may be null.
- Same crypto directory `CngHelpers.cs:25–59` and `CngKey.StandardProperties.cs:72–83,113–179`:
  primitive missing=>zero, CLR missing=>false and KeyName CLR-marker short circuit
  explain why ordinary getters cannot establish independent evidence.
- Common `KeyPropertyName.cs:8–25,32–36` lists exact property strings.
- `CngPropertyOptions.cs:15–17`: None0, CustomProperty0x40000000, Persist0x80000000.
- Common `Interop/Windows/NCrypt/Interop.ErrorCode.cs:16`: NTE_NOT_FOUND0x80090011.

Contract reference:
https://learn.microsoft.com/en-us/windows/win32/seccng/key-storage-property-identifiers
and public CngKey.GetProperty/HasProperty API contracts. Export Policy's persisted-key
wording does not guarantee unnamed-key support. This design does not invent it.

### Fixed read plan (proposal, not executable defaults)

| Metadata | Exact property / read options | Proposed required returned shape/value | Meaning limit |
| --- | --- | --- | --- |
| Export policy | `Export Policy`, None | exactly4 bytes, little-endian unsigned0 | observed None metadata, no export-denial proof |
| Usage | `Key Usage`, None | exactly4 bytes, unsigned2 (Signing only) | no extra bits or AllUsages accepted |
| Scope flags | `Key Type`, None | exactly4 bytes, unsigned0 | no machine flag0x20, no unknown flags; current-user metadata only |
| Bit size | `PublicKeyLength`, None | exactly4 bytes, unsigned256 | no fallback to Length if unavailable |
| Algorithm | `Algorithm Name`, None | exact NUL-terminated UTF16LE `ECDSA_P256` | strict whole-value comparison, no aliases |
| Group | `Algorithm Group`, None | exact NUL-terminated UTF16LE `ECDSA` | no missing/unknown algorithm group |
| CLR marker | `CLR IsEphemeral`, CustomProperty | exactly1 byte equal1 | CLR bookkeeping only, not native nonpersistence |
| Native name observation | `Name`, None | classify ABSENT or exactly one UTF16LE NUL as no-name observation; any nonempty value is mismatch | absence/empty is not proof of no persisted state |

All required properties except the explicitly classified Name must be present,
correct shape and exact expected value to continue. Name has a separately defined
ABSENT outcome; it never becomes `None`, a numeric zero or a security PASS.
A provider returning NTE_NOT_SUPPORTED for Name is ERROR/UNKNOWN, not ABSENT.
Do not request Unique Name, Security Descr, Provider Handle or enumerate/reopen keys.
An unnamed key may reject properties: stop as INCONCLUSIVE, not native incompatibility
proof and never retry with a named key. The one-shot attempt is consumed.

Explicit creation provider remains MicrosoftSoftwareKeyStorageProvider, flags0,
ExportPolicy None, Usage Signing, null name, parent0/null UI/no custom parameters.
Requested provider identity is not actual registration/module proof. No new provider
metadata probe is smuggled into this key-only plan. Any future observed Provider
getter is advisory unless separately reviewed; it cannot replace strict required
readbacks or establish registration/ACL. Existing-key ECDsaCng wrapper internally
reads metadata; those inherited accesses remain part of the audited native chain.

### Classification and fail-closed sequence

For each allowlisted identifier, make one public GetProperty acquisition, then one
GetValue copy. Record only property enum/status/expected-shape outcome, never bytes.

- CryptographicException with exactly NTE_NOT_FOUND => **ABSENT**, never zero.
- Other CryptographicException => **PROVIDER_ERROR** with sanitized HRESULT only;
  unsupported is not absent. Unknown exception => **ERROR/INCONCLUSIVE**, no raw message.
- GetValue null/empty => **MALFORMED** for DWORD/marker/string-required fields;
  Name's exact two-byte NUL is a distinct EMPTY_NAME observation, not null bytes.
- Wrong length, odd UTF16, absent/interior/trailing terminator, malformed encoding,
  embedded NUL, extra bytes or over-budget response => **MALFORMED/STOP**.
- Exact shape but wrong value/extra bits => **MISMATCH/STOP**.
- Exact shape/value => **OBSERVED_EXPECTED_METADATA**, never protection PASS.

Use deterministic little-endian unsigned32 decoding, not platform-dependent casts,
default enum values, string trimming or permissive replacement decoding. For fixed
algorithm strings compare complete canonical UTF16 bytes/shape; do not normalize.
Name nonempty => mismatch without printing its value. A nonempty oversized name
remains malformed and stops, without path/key-name disclosure.

HasProperty may be omitted: GetProperty already distinguishes missing. If later
included for a separately approved reason, true is only an instantaneous existence
check; false=ABSENT, errors=ERROR. A later missing/malformed fetch stops. No check-
then-use stability, retry, HasProperty(false)=>zero, fallback property/provider or
repair is allowed. Public API itself performs two native reads; one invocation
cannot promise atomic provider data or exactly one NCrypt call.

**Allocation limitation:** internal GetProperty allocates from provider size BEFORE
caller sees bytes; constructor/GetValue clone too. Exact-size/cap checks constrain
accepted results and retained/report data, not transient managed/native allocations.
No strict4-byte preallocation or whole-process memory cap can be promised through
this API. Proposed max64bytes per returned metadata value is a POST-call acceptance
bound only. OOM/native hang are not safely recoverable with a catch/Task timeout.
This limitation requires an owner design choice before Gate I; no new interop
workaround is authorized. Native property success/length may still be provider-specific.

### Proposed pure negative matrix — NOT IMPLEMENTED / NOT RUN

All cases use fabricated byte/status inputs to a pure classifier; no CNG API or
crypto fixture. Fake status injection is never native evidence or consent.

| Case | Required expected classification |
| --- | --- |
| Export Policy missing/NTE_NOT_FOUND | ABSENT; no None observation |
| Export Policy4 zero bytes | OBSERVED_EXPECTED_METADATA only |
| Export Policy null/0/1/3/5 bytes | MALFORMED |
| Any allow-export/archive/unknown bit | MISMATCH |
| Key Type missing vs4 zero bytes | ABSENT vs observed user-scope metadata |
| Key Type0x20/unknown bits | MISMATCH |
| Usage2 vs0/AllUsages/Signing+other | observed vs MISMATCH |
| PublicKeyLength256 vs missing/0/384 | observed vs ABSENT/MISMATCH; no Length fallback |
| CLR marker1 vs missing/error/0/2/multibyte | observed bookkeeping vs STOP |
| Exact algorithm/group UTF16LE vs alias/odd bytes/extra NUL | observed vs MALFORMED/MISMATCH |
| Name absent vs two-byte NUL | distinct ABSENT vs EMPTY_NAME observations |
| Name unsupported/null/zero-byte/nonempty | ERROR/MALFORMED/MISMATCH; no value logged |
| HasProperty true then fetch absent | STOP, no retry |
| NTE_NOT_SUPPORTED/buffer-too-small/other HRESULT | PROVIDER_ERROR; no absence substitution |
| Over-budget/allocation exception | STOP/INCONCLUSIVE; no cap guarantee asserted |
| Exception containing path/key material | output only phase/type/allowlisted HRESULT |
| Success metadata plus cleanup unknown | no overall security/cleanup PASS |
| Reentry/no consent/invalid plan | native call count0 in future fake preflight test |

Sufficient only for limited W1 metadata report: request ledger plus attributable
present, exact-shape observations and public leaf fields after separate execution
approval. Insufficient for export-denial causality, owner DACL, OS nonpersistence,
cleanup receipt, chain/TLS/Android/production policy. ACL always NOT ESTABLISHED.
No returned CngProperty implies permission to export private data.

## P3: deterministic host design (not a project/configuration)

Proposed Windows x64, framework-dependent `net10.0-windows`, no desktop/UI framework,
no production/project references, no solution/CI entry. Existing production runtime
10.0.14 is untouched. Owner selects proposed SDK10.0.402/runtime10.0.13 before Gate I.

- Nested global.json: SDK10.0.402, rollForward `disable`, allowPrerelease false;
  fixed CWD in the isolated project directory. CLI muxer uses CWD/ancestor search;
  MSBuild resolver may use project/solution directory. No root invocation assumption.
- Project intent: RuntimeFrameworkVersion10.0.13, RollForward Disable, PlatformTarget
  x64, framework-dependent/SelfContained false; generate runtimeconfig. Optional RID
  win-x64 only if independently reviewed without changing self-contained semantics.
- Required generated runtimeconfig: framework **Microsoft.NETCore.App** version
  **10.0.13**, rollForward **Disable**, no extra framework, dev probing or host-policy
  fallback. TFM is not a servicing pin; project properties alone are not output proof.
- Propose reviewed x64 dotnet host chosen by an exact PRIVATE owner-local path/hash;
  no fabricated path now and no private path in shared evidence. Later VSTest
  RunConfiguration DotnetHostPath should select that host explicitly; TargetPlatform
  X64, correct .NETCoreApp version10.0, one exact test assembly. No AnyCPU default.
- VSTest17.14.1 source `490850ae3fdc1b470e3804ceab4f6a41cf89ae51`,
  `DotnetTestHostManager.cs:198–200,243–268,279–339,435–479` supports custom host and
  source runtimeconfig path selection. It also has runner/config/deps fallback paths
  (`348–425`) which must be rejected, not relied upon. Legacy Major roll-forward
  (`486–493`) applies only to target below3.0; it does not justify net10.0 fallback.
- Proposed direct packages remain Microsoft.NET.Test.Sdk17.14.1, xunit2.9.3 and
  xunit.runner.visualstudio3.1.5; testhost17.14.1 expected, not resolved/proven.
  Package-source/strong-name/source correspondence, all transitive runtime/adapter/
  analyzer/generator/build assets require Gate B review. No auto-upgrade/adapter search.
- Audit presence (never dump values) of DOTNET_ROLL_FORWARD, legacy
  DOTNET_ROLL_FORWARD_ON_NO_CANDIDATE_FX, DOTNET_ROLL_FORWARD_TO_PRERELEASE,
  DOTNET_ROOT/architecture variants, DOTNET_HOST_PATH, DOTNET_STARTUP_HOOKS,
  DOTNET_ADDITIONAL_DEPS, DOTNET_SHARED_STORE, DOTNET_MULTILEVEL_LOOKUP, COMPlus_*
  host/profiler overrides, CORECLR_* profiler overrides, VSTEST_* paths/root controls,
  MSBuildSDKsPath/MSBUILD_EXE_PATH, NuGet/plugin sources, adapter/runsettings and PATH.
  Review all additional relevant overrides in exact resolved host version; list is
  not exhaustive. Unexpected override => BLOCK, no unsetting global variables here.
  Any later process-scoped sanitized launch configuration needs separate approval.

Docs: https://learn.microsoft.com/en-us/dotnet/core/tools/global-json ;
https://learn.microsoft.com/en-us/dotnet/core/versions/selection ;
https://learn.microsoft.com/en-us/dotnet/core/project-sdk/msbuild-props .
Installed versions != selected SDK != parent CLI runtime != child testhost runtime.

## Static manifest / independent Gate I, B and E requirements

**I (not granted):** owner resolves design decisions in owner sheet; exact source
allowlist remains W1-PREP's nine files under nonexistent
`tests/PeripheralBatteryCompanion.W1Offline.Tests/`: csproj, W1OfflineLifecycleTests.cs,
W1PreflightPlan.cs, W1NativeLifecycle.cs, W1Evidence.cs, AssemblyInfo.cs,
xunit.runner.json, global.json, NuGet.Config. Exact csproj name is
PeripheralBatteryCompanion.W1Offline.Tests.csproj. No extra runsettings/script now;
any later runner-settings artifact requires its own expanded allowlist review.
Explicit source items/no legacy links/default compile globs. No static/module/test
constructor/fixture/discovery/teardown native effects, retries or parallel cases.
No adapter default/native construction before the method's consumed one-shot gate.

**B (not granted):** separately authorize each restore/build/discovery phase; each
can execute tasks/code. Before restore, review SDK/import/source configuration and
package execution risk. Set proposed Directory.Build import disables BEFORE Sdk.props;
independently exclude/inventory Directory.Packages, ancestor NuGet/global/SDK/user
imports, custom InitialTargets/AfterTargets and buildTransitive/generators. No app/
legacy test reference. After approved restore, review lock/assets/package hashes and
all generated targets before build; after build inspect all artifacts as inert data
before any separately approved discovery/host launch. No binary loading as static audit.

Manifest fields to record locally/sanitize for sharing: repo commit + exact source
allowlist/hashes; VMR/runtime source commits; chosen SDK root/identity/hash + effective
CWD/global.json; approved MSBuild import graph and task/package hashes; exact framework
reference/resolved runtime packs; lock/asset/deps/runtimeconfig hashes+content rules;
x64 testhost/runner/adapter/executable hashes; selected framework/hostpolicy/coreclr/
System.Security.Cryptography assembly expected version/commit/hash mapping; final
process arguments/environment control plan; logger/output privacy/limits; no unresolved
fallback, dependency, initializer or phase authorization. No such W1 manifests exist yet.

Source/package declarations and DLL filenames are not actual loaded-host proof.
If Gate B includes a separately reviewed pure no-crypto host-intake action, collect
sanitized selected runtime/architecture/assembly provenance there; otherwise mark
unobserved and retain an E block. Do not implicitly authorize discovery by asking for
build proof. Never execute W1 native setup to discover runtime identity.

**E (not granted):** after independent built-bit/runtime review and exact inputs,
approve ONE exact source/binary/config/hash/host/context/command/selector. Preflight
must match evidence and refuse uncertainty BEFORE provider open. Exact future case:
`PeripheralBatteryCompanion.W1Offline.Tests.W1OfflineLifecycleTests.SingleShot_UnnamedNonExportableP256_PublicLeaf_NoStore`.
No class wildcard/negative-only filter; zero/multiple matches or unexpected modules
stop. A version self-check is defense in depth, not sole source mapping proof.
No implementation/build/discovery/native command is emitted by this document.

D3.3A incident:11 native certificate-generation and two secret-sample cases ran
outside authorization. Never run old test.ps1/Option2/fixture previews/unfiltered
legacy tests to establish a baseline; preserve that evidence and old resources.

**NATIVE NOT RUN / TESTS NOT RUN / LAN NOT RUN / PRODUCTION SECURITY NOT PROVEN.**

Stop for owner review. No automatic Gate I/B/E, merge, release or Android activity.
