# E3.3 — W1Offline test-runner isolation hardening

**Current status: scoped Gate B evidence recorded; independent owner acceptance pending.**
**Gate B execution was authorized only for the scoped verification below. Gate E: BLOCKED and UNAUTHORIZED.**

Earlier source-only and verification records below retain their original scope;
the final dated verification-gap closure section is the current evidence snapshot.

## Scope

The original committed patch addresses two source-configuration omissions in the isolated W1Offline test project. The separately authorized local source-only completion below adds fail-closed harness hardening and regression source. Neither scope authorizes restore, build, test discovery, test execution, or native lifecycle execution.

Base: `feature/stage-e3-2-pure-security-policy`
Working branch: `feature/stage-e3-3-test-runner-isolation`

## Original committed changes (through 5681dc359c79669ed6c6873b3c15facb708768a0)

In `tests/PeripheralBatteryCompanion.W1Offline.Tests/PeripheralBatteryCompanion.W1Offline.Tests.csproj`:

1. Explicitly include `AssemblyInfo.cs` in the `Compile` items. The project disables default compile items, so the existing assembly-level xUnit collection behavior attribute was otherwise not guaranteed to reach the test assembly.
2. Explicitly include `xunit.runner.json` as a `None` item with `CopyToOutputDirectory="PreserveNewest"`. The project disables default items, so the existing runner configuration was otherwise not guaranteed to be copied beside the test assembly.

The existing files remain unchanged:
- `AssemblyInfo.cs`: disables test parallelization and sets maximum parallel threads to one.
- `xunit.runner.json`: disables assembly and collection parallelization, sets one maximum parallel thread, and disables theory pre-enumeration.

No duplicate compile item was added in the project file. The assembly source is listed once.

## Static directory inventory

A read-only GitHub contents listing of `tests/PeripheralBatteryCompanion.W1Offline.Tests/` was reviewed at this branch. All entries were accounted for:

| File | Intended handling | Reason |
|---|---|---|
| `AssemblyInfo.cs` | Explicit `Compile` | Assembly-level xUnit collection behavior attribute |
| `GateEAuthorizationGate.cs` | Explicit `Compile` | Test subject / authorization guard source |
| `GateEAuthorizationGateTests.cs` | Explicit `Compile` | Pure guard unit tests, with process-local environment/latch effects |
| `W1Evidence.cs` | Explicit `Compile` | Evidence model and metadata classification |
| `W1NativeLifecycle.cs` | Explicit `Compile` | Protected native lifecycle source; inclusion does not authorize execution |
| `W1OfflineLifecycleTests.cs` | Explicit `Compile` | Contains the protected native test, which must remain skipped |
| `W1PreflightPlan.cs` | Explicit `Compile` | Plan and permit/preflight source |
| `W1PreflightPlanTests.cs` | Explicit `Compile`, added in local completion | Byte-only serialization, path and inert permit regressions |
| `xunit.runner.json` | Explicit `None` with output copy | xUnit runner configuration required beside test assembly |
| `NuGet.Config` | Not a compile/output item | Restore-time package source configuration, consumed by NuGet based on project location; no restore authorized |
| `global.json` | Not a compile/output item | .NET SDK selection policy discovered from the working-directory ancestry; it is not an assembly runtime config |

This inventory is a source-tree classification only. It does not prove which SDK or NuGet configuration a future process resolves, which files a testhost loads, or that runner settings take effect. Parent-level SDK/MSBuild/NuGet imports and environment influences remain outside this directory inventory.

## Preserved constraints

- Target framework remains `net10.0-windows`.
- Runtime framework remains `10.0.13`; roll-forward remains disabled.
- x64 target, framework-dependent execution and existing package versions remain unchanged.
- Default item discovery remains disabled.
- Existing native lifecycle test retains its explicit xUnit Skip attribute.
- The original committed patch changed no native lifecycle source, authorization guard, permit logic or report logic. Local completion preserves native lifecycle/report source, native Skip and approval/provenance holds; authorization and permit hardening is described below. Gate B/E state remains unchanged.
- No build, restore, test discovery, test execution or native API was run as part of this source patch or static inventory.

## Static acceptance

| Check | Result |
|---|---|
| Assembly-level xUnit configuration source exists | PASS — source inspection |
| AssemblyInfo.cs explicitly listed once in Compile | PASS — project file |
| Runner JSON explicitly copied to output | PASS — project file |
| Runner JSON content consistent with single-thread/no-parallel policy | PASS — source inspection |
| All files in test-project directory accounted for | PASS — static directory inventory |
| NuGet.Config classified as restore-time config | PASS — static classification only |
| global.json classified as SDK-selection config | PASS — static classification only |
| Existing native test Skip preserved | PASS — source unchanged |
| Runtime runner actually loads JSON | NOT VERIFIED |
| Compiled assembly contains expected assembly attribute | NOT VERIFIED |
| Testhost executes serially in practice | NOT VERIFIED |
| Effective SDK/NuGet imports and binary provenance | NOT VERIFIED |
| Build/restore/discovery/test execution | NOT RUN |

## Remaining E3.3 work

1. Independently review the expanded local source diff and proposed verification below. Project properties/packages are unchanged; the only additional project item is the new regression source.
2. Resolve provenance/import/dependency review prerequisites and obtain exact Gate B authorization before any toolchain invocation.
3. Verify output copy, compiled assembly attribute, runner loading, process lifetime and serial execution under that authorization. None has been demonstrated by source inspection.
4. Reconcile independent review findings. PR #30 still describes only the original two-file patch; no PR update is authorized in this completion.

## Local source-only completion

Recovery confirmed the expected branch/HEAD, exactly three intentional unstaged tracked changes and four pre-existing untracked files. All were preserved. No prior E3.3 runtime baseline exists: recorded status is NOT RUN / NOT VERIFIED, not a passing test result. E3.2 Android JVM results are not Windows isolation evidence.

The preserved local hardening makes `EnsureAuthorized` always throw, including when a legacy environment token and opaque payload are supplied. Its process-local latch trips on the first denied attempt. `GateEPermit.TryAcquireFromFile` always returns false/null without file access or native/cryptographic acquisition; `Matches` always returns false. `TryConsume` always returns false. `SerializeCanonical` returns bytes separately from `ComputeDigest`, and rejects non-fully-qualified destinations. `ComputeDigest` still calls platform-backed `SHA256.HashData`.

No further functional refactoring was needed. Source review of the existing call chain finds unconditional native-approval and provenance holds before lifecycle admission, plus the explicit native test Skip. No authorization bypass was introduced. This is source reasoning, not proof against reflection, hostile code, binary substitution or altered imports.

Regression completion replaces guard-test wall-clock reads with a fixed synthetic timestamp and preserves the four existing local assertions/cases. New cases cover token/payload combinations, future/malformed/missing plans, and the two unconditional holds without invoking the lifecycle. Every environment mutation restores the prior value in `finally`; fixture construction/disposal resets only the test latch. Each theory row must receive its own fixture. These measures do not grant separate processes, survive abrupt process termination, or protect against unrelated same-process environment mutation. No retry is permitted.

`W1PreflightPlanTests` covers valid/invalid/missing plans across inert permit operations and repeat calls; null/empty/relative/drive-relative/root-relative destination rejection; and literal canonical bytes for all fifteen fields. It also checks repeat serialization after caller mutation of the returned array and existing trailing-backslash normalization. The synthetic path is never opened. Fully qualified syntax does not verify ACLs, reparse points, ownership or report safety; canonical serialization is not complete admission validation.

The existing `FrozenTestVector_CanonicalSerializationAndDigest_MatchesSpecification` and its exact SHA-256 assertion are unchanged. It invokes `ComputeDigest` and is **not provider-free**. It is excluded from the non-native proposal. No digest was computed during completion.

| Regression source | Intended assertions | Execution status |
|---|---|---|
| Existing four guard cases | unconditional denial, missing/expired inputs, repeated denial latch | NOT RUN |
| Legacy token/payload theory | absent, empty, invalid and `1` token cannot authorize | NOT RUN |
| Invalid-plan theory | future, malformed and missing plan denied without serialization | NOT RUN |
| Approval/provenance hold case | both holds throw; no lifecycle invocation | NOT RUN |
| Inert permit case | acquisition false/null; matching and consumption false, including repeats | NOT RUN |
| Destination theory | both validation and serialization reject six non-qualified inputs | NOT RUN |
| Literal byte vector | exact encoding/order/lengths, independent returned arrays, normalization | NOT RUN |
| Existing frozen digest case | original SHA-256 assertion preserved; separate crypto scope | NOT RUN; excluded |

Only text/source and Git diff inspections were performed. No compiler, restore, testhost, discovery, native crypto, CI or production security functionality ran. Regression source has not been compilation-validated. The ordinary `test.ps1` excludes W1Offline, and CI invokes that script; neither is an E3.3 acceptance command.

## Future non-native Gate B verification proposal — NOT AUTHORIZED / NOT EXECUTED

Review first: exact final source revision/diff, seven existing compile items plus the added test item, unchanged native Skip/holds, no module/static initialization invoking native code, every allowlisted case and its call graph, test adapter/discovery hooks, SDK 10.0.402 and runtime 10.0.13 provenance, package versions and full transitive closure/hashes, effective SDK/MSBuild imports and targets, generators, user/machine NuGet configuration/plugins, environment overrides and testhost launch behavior. This project disables directory imports but still imports SDK/package targets. Its local `global.json` is selected by working-directory ancestry; run from this test-project directory only.

There is no project-local tracked lockfile in the inspected inventory. Reviewers must resolve exact dependency closure/provenance before approving restore; do not invent a locked-restore guarantee. If immutable dependency resolution cannot be established, stop and authorize a separate source-only locking proposal rather than silently changing packages or running restore. Built-in SDK/build tooling may itself use cryptography internally: the proposed scope excludes application digest/native security operations, not a claim that tooling is crypto-free.

After those prerequisites, the owner may approve these exact commands, one at a time from a fresh ordinary PowerShell process. They are a proposal only. `Push-Location` below is relative to the repository root and does not change Git branches. Generated obj/bin/results artifacts and approved NuGet network/cache writes need inclusion in that future authorization. No broad solution/test.ps1/CI invocation, implicit restore, native test, frozen digest test or retry is allowed.

```powershell
# NOT EXECUTED. Requires separately approved Gate B scope and prerequisites above.
Push-Location 'tests/PeripheralBatteryCompanion.W1Offline.Tests'
dotnet --info
dotnet restore .\PeripheralBatteryCompanion.W1Offline.Tests.csproj --configfile .\NuGet.Config --no-cache --verbosity diagnostic
dotnet build .\PeripheralBatteryCompanion.W1Offline.Tests.csproj --no-restore --configuration Release --verbosity diagnostic
Get-Content .\bin\Release\net10.0-windows\xunit.runner.json
Compare-Object (Get-Content .\xunit.runner.json) (Get-Content .\bin\Release\net10.0-windows\xunit.runner.json)
dotnet test .\PeripheralBatteryCompanion.W1Offline.Tests.csproj --no-build --no-restore --configuration Release --filter 'FullyQualifiedName~PeripheralBatteryCompanion.W1Offline.Tests.GateEAuthorizationGateTests.|FullyQualifiedName~PeripheralBatteryCompanion.W1Offline.Tests.W1PreflightPlanTests.' --logger 'trx;LogFileName=e3-3-nonnative.trx' --results-directory .\test-results\e3-3-nonnative --diag .\test-results\e3-3-nonnative\vstest.log -- RunConfiguration.MaxCpuCount=1
Pop-Location
```

Stop on any nonzero exit, unexpected import/package/source/test, missing output, skipped allowlisted case or uncertain provenance. Do not proceed to later commands after failure. The test command entails adapter discovery even with a filter; review the entire discovery surface first. The trailing-dot class prefixes allowlist only the two reviewed classes. Do not use a negative-category filter or run the whole assembly. Proposed source inventory is 21 selected cases (13 guard, 8 preflight); reconcile adapter output with each theory row, never treat an unexpected zero or partial run as success.

Expected evidence: exact revision plus local diff identity, approved tool/runtime/package hashes and resolution records, exit status per command, sanitized build/import and testhost diagnostics, copied JSON comparison, TRX names/outcomes/counts, actual loaded testhost/runtime/adapter identities and fresh testhost lifetime. Review logs locally before sharing; raw diagnostic logs can contain paths, environment values or private feed information. Do not dump environment variables or credentials. No rerun/retry or latch reset outside the reviewed fixtures.

Output copying alone does not prove JSON loading. Independently inspect compiled metadata without loading/invoking the assembly to verify the collection attribute, and collect runner configuration-loading/serial scheduling evidence and process lifetime evidence under a separately reviewed observation procedure. The exact inspection tool/command must be approved before use; none is invented or run here. Current JSON disables diagnostic messages, so the proposed commands may not supply sufficient runner-loading evidence. Missing evidence leaves those acceptance rows NOT VERIFIED; it does not justify changing JSON or invoking additional tools automatically. `MaxCpuCount=1` and xUnit parallelization flags limit concurrency, not security isolation. Do not load the test DLL into the Codex/PowerShell process to inspect attributes.

Gate E still needs a separately designed/reviewed authenticated exact-operation permit, expiry/clock policy, trusted atomic one-time nonce consumption with rollback resistance, exact source/binary/runtime provenance, resource/time bounds, owned cleanup and report-destination safety. Production TLS, DPAPI, Keystore, enrollment, listeners and connectivity are outside this verification scope. Successful non-native tests cannot authorize any of them.

## Decision

This patch fixes the two identified project-item omissions at source level. The static directory inventory accounts for all files in the test-project directory. It does **not** establish runtime isolation or make the native test safe to run.

Keep Gate E blocked and the native test skipped. No gate transition or merge is authorized by this document.

## Gate B local verification update (2026-11-01)

The owner subsequently approved Gate B offline build and nonnative test execution. This section supersedes earlier NOT RUN / NOT AUTHORIZED statements only for these specifically completed local operations. It does not change the original source-only completion history.

- Offline NuGet restore succeeded using the local package cache. All 13 cached package archives matched official NuGet downloads by SHA-512; the difference between archive hashes and assets-file contentHash values was observed and is not treated as proof of tampering.
- Debug build with --no-restore succeeded: 0 warnings, 0 errors.
- Nonnative test run with --no-build --no-restore and Category!=GateE_Native_Protected succeeded: 22 passed, 0 failed.
- git diff --check passed.
- The native-protected test was excluded. No Gate E native lifecycle execution was authorized or performed.
- These results do not establish native cryptographic authorization, complete runner-isolation evidence, or Gate E readiness.
- Verification was local; GitHub CI has not been claimed as passing for this change.

## E3.3 Gate B acceptance reconciliation (2026-11-01)

This is the current status snapshot and supersedes earlier *prospective* / NOT VERIFIED checklists where subsequent evidence exists. Historical source-only statements above remain accurate for their original time and scope.

### Completed evidence (local unless specified)

- Offline restore succeeded with 13 cached NuGet archive SHA-512 comparisons against official NuGet downloads. This does not attest every toolchain component or loaded binary.
- Debug build completed with zero warnings and zero errors; git diff --check passed.
- A filtered local xUnit run completed with **22 passed, 0 failed**. The protected native lifecycle test was excluded; it remained explicitly skipped in source.
- Compiled test assembly metadata was inspected without loading or executing the test DLL; the xUnit assembly-level serial collection behavior attribute was present.
- Source/output xunit.runner.json SHA-256 hashes matched. In an isolated temporary copy, 86 copied files had zero hash mismatches before the temporary runner JSON was changed to enable diagnostics.
- The isolated dotnet vstest run with Category!=GateE_Native_Protected reported 22 passed, runner adapter 3.1.5, "parallel test collections = off", successful exit, and zero remaining testhost processes after the run. Diagnostic output supports runner activity but **does not independently prove a JSON file-open event or configuration precedence**, since the compiled assembly attribute also disables parallelism.
- Two Windows GitHub Actions checks succeeded at commit 8af4c4eff590129296ee857dca8166f7920872a4. Two d33a-code-only jobs were skipped, not passed. These Windows jobs are not represented as a substitute for the isolated W1Offline local test evidence.

### Test-scope reconciliation

The earlier source-only proposal named an allowlist of guard/preflight classes and excluded the existing frozen digest test. The later executed run instead used a negative category filter, Category!=GateE_Native_Protected, and therefore included **FrozenTestVector_CanonicalSerializationAndDigest_MatchesSpecification**, which invokes platform-backed SHA256.HashData. The run was **nonnative-lifecycle**, not crypto-free and not identical to the original proposed allowlist. Record this deviation explicitly; do not retroactively claim the original allowlist was executed. The evidence establishes 22 passing selected tests, not permission for native cryptographic lifecycle execution.

### Source review findings

- GateEAuthorizationGate.EnsureAuthorized always denies and trips its process-local latch on the first denied attempt, regardless of legacy environment token or opaque permit payload.
- GateEPermit.TryAcquireFromFile, GateEPermit.Matches and W1PreflightPlan.TryConsume fail closed.
- W1NativeLifecycle.Execute begins with unconditional native approval and execution-provenance holds before any native key creation; the protected test has an explicit Skip and calls the native approval hold first.
- RunAuthorizedBody has an unconditional report destination verification hold before FileStream creation, although this does not imply that entering the native body is authorized.
- AssemblyInfo.cs and xunit.runner.json are explicitly included in the project. Serial scheduling settings limit concurrency but are not security isolation.
- No authorization bypass was identified in the reviewed entry paths. This is a scoped source review, not an independent security certification.

### Acceptance disposition

**Gate B technical evidence: substantially complete for the checks performed, with documented limitations.** Exact runner JSON file-open/config precedence was not independently traced; exact testhost lifetime was not measured beyond the completed-run observation. The earlier allowlist deviation remains disclosed. Formal **independent reviewer approval is pending**: GitHub reported zero submitted reviews and zero approvals. Do not mark formal Gate B acceptance complete or merge PR #30 until the required independent review and any outstanding evidence criteria are resolved.

**Gate E remains BLOCKED.** No protected native lifecycle test was executed or authorized. No Gate E transition, protected-test unskip, or native operation is authorized by this review.

## Gate B verification-gap closure (2026-11-01, owner acceptance pending)

The existing checkout was fetched and fast-forwarded from `8af4c4e` to exactly
`8b31236d808f81abd1b8a1fb9c455ade5dc59f82`. Only the expected documentation commit
was added. Implementation is on `codex/e3-3-gate-b-evidence`, based on that revision.
All four pre-existing untracked files were left untouched. The older 22-pass
negative-filter result remains historical evidence, includes the platform-backed
SHA-256 digest test, and is not represented as crypto-free or as this new allowlist.

### Execution boundary and explicit allowlist

`tests/PeripheralBatteryCompanion.W1Offline.Tests/GateB/PeripheralBatteryCompanion.W1Offline.Tests.csproj`
builds a separate `PeripheralBatteryCompanion.GateB.Tests.dll`, preserving framework,
runtime, architecture and package versions. Its five explicit source items are the
existing assembly attribute, actual authorization guard, actual preflight plan,
existing byte-only preflight tests, and new token-free denial tests. It has no
reference to the original test project. Neither `W1NativeLifecycle.cs`,
`W1OfflineLifecycleTests.cs`, nor the token-mutating guard test class is compiled.
Thus protected native lifecycle and frozen digest tests are absent from discovery,
not merely excluded by a negative filter. `ComputeDigest` remains in the linked
plan source but is never called by this harness. Ordinary SDK/adapter tooling is
not claimed to be crypto-free or OS-sandboxed.

The eight-method equality allowlist is exported in
[allowlist.txt](evidence/e3-3-gate-b/allowlist.txt). It selects 15 cases: seven
token-free denial/hold cases and eight existing preflight cases. The script checks
TRX method identity, per-method row counts, all-pass outcomes and the exact total.
It refuses a pre-existing nonempty `PBC_GATE_E_AUTHORIZED` value without logging or
changing it. No token/environment mutation is performed. Six legacy-token-writing
cases from the prior 21-case proposal are intentionally not run; equivalent
non-token denial cases in this dedicated harness do not prove token mutation or
restoration behavior. Original tests, native Skip, guard implementations and
provenance/report holds are unchanged.

The dedicated project deliberately retains the `PeripheralBatteryCompanion.W1Offline.Tests.csproj`
basename so both it and the original project remain excluded by the existing
`test.ps1` discovery rule. Lightweight Windows CI calls that same script. Neither
general runner nor CI was changed to execute this sensitive harness automatically.

### Reproduction and measured results

From the repository root in PowerShell 7, the commands used were equivalent to:

```powershell
Push-Location tests/PeripheralBatteryCompanion.W1Offline.Tests/GateB
# Inherits the parent's pinned SDK configuration: 10.0.402, rollForward disabled.
dotnet --info
$assets = Get-Content ../obj/project.assets.json -Raw | ConvertFrom-Json
$packageCache = @($assets.packageFolders.PSObject.Properties.Name)[0]
dotnet restore ./PeripheralBatteryCompanion.W1Offline.Tests.csproj --source $packageCache --ignore-failed-sources --verbosity minimal
dotnet build ./PeripheralBatteryCompanion.W1Offline.Tests.csproj --no-restore --configuration Debug --verbosity minimal -p:UseSharedCompilation=false
Pop-Location
./tests/PeripheralBatteryCompanion.W1Offline.Tests/GateB/verify.ps1
```

Stop after any failed command; no automatic retry, protected test or authorization
token is allowed. The cache path is resolved locally and never committed. Restore
used that sole filesystem source and cached packages, not a remote feed. SDK
10.0.402/MSBuild 18.9.15, Windows 10.0.26200 x64, runtime 10.0.13, VSTest 18.7.0 and
xUnit adapter 3.1.5 were observed. Restore succeeded; Debug build succeeded with
zero warnings/errors. The successful final verifier session was
`20261101T132922366Z`: **15 passed, 0 failed, 0 skipped**. No full application suite
or native/manual device checks were run. Final script parser and diff checks passed.
The dedicated/original assets files list the same 13 package identities and matching
content-hash strings; [dependency-closure.json](evidence/e3-3-gate-b/dependency-closure.json)
records that comparison. This is assets consistency, not fresh package or runtime
attestation. The prior official-download comparisons remain historical evidence.

The script's exact fixed `dotnet vstest` arguments are retained in each process
record. It runs baseline discovery, JSON-probe discovery, then one allowlisted
execution using the unchanged baseline JSON. Diagnostic logging is enabled on the
copied probe JSON only; no original config or build output is edited. Raw VSTest,
testhost diagnostics and TRX remain under `test-results/e3-3-gate-b/<session>/`.
Sanitized console/process/case evidence is checked in under
[docs/evidence/e3-3-gate-b](evidence/e3-3-gate-b/README.md); it omits machine/user
identity and raw command lines. Review raw local diagnostics before sharing.

### JSON consumption observed independently of the assembly attribute

Two isolated output copies each contained 86 files. Byte comparisons found every
baseline file identical to build output and only `xunit.runner.json` different in
the probe. The assembly binaries and therefore their assembly-level settings were
identical. Source JSON uses `preEnumerateTheories=false` and diagnostics off; the
probe sets pre-enumeration and diagnostics true, keeping parallelization flags.

Observed baseline discovery: **8 method-level cases**, without theory data rows.
Observed probe discovery: **15 enumerated cases**, including all three plan rows
and six destination rows; additional diagnostic details appeared. This controlled
JSON-only behavior change demonstrates runner JSON consumption independently of
the unchanged assembly attribute. It matches the documented
[xUnit configuration semantics](https://xunit.net/docs/config-xunit-runner-json).
It is behavioral evidence, not an OS file-open trace. It does not separately prove
every JSON key, parallelism-key precedence over the attribute, or parallel
execution safety. No runsettings override was used.

### Process lifetime observations and limitations

PowerShell monitor PID **21644** launched separate CLI/VSTest/testhost processes.
It did not load the test DLL. CIM supplied process IDs and parent IDs; retained
OS process handles supplied start/exit timestamps and successful termination.
OS event subscriptions were unavailable (`Access denied` for baseline/execution,
`Call cancelled` for the final probe), so the
successful verifier explicitly reports `CimPollingWithProcessHandles`. The
`EventUtc` field is polling observation time; `StartUtc`/`ExitUtc` are OS process
times, not inferred from a final snapshot. All timestamps below are UTC.

| Invocation | CLI PID | Testhost PID | Host parent PID | Host start | Host exit | Exit | Remaining hosts |
|---|---:|---:|---:|---|---|---:|---:|
| Baseline discovery | 38176 | 42568 | 17040 | 13:29:27.1154789 | 13:29:28.5317671 | 0 | 0 |
| Probe discovery | 32836 | 22864 | 176 | 13:29:30.9083086 | 13:29:32.0012134 | 0 | 0 |
| Allowlisted execution | 34180 | 21972 | 28568 | 13:29:34.0786806 | 13:29:35.3222757 | 0 | 0 |

Process JSON includes the observed intermediate VSTest parent chain. Each observed
testhost had a retained handle confirming exit, a stop record and no remaining
testhosts in the final system snapshot. Fresh hosts across invocations are observed;
separate processes per test case are not claimed. This is lifecycle observation,
not a Windows security sandbox or containment guarantee. Polling can miss
short-lived descendants and cannot establish complete process-creation history.
No privilege elevation, broad termination or test DLL load in the monitor occurred.

### Failed observations retained

Five earlier verifier attempts failed before the allowlisted execution stage:
event access denied before runner launch; two attempts lost exit metadata because
a process handle was not retained; one misclassified a testhost with an empty
command line; one hit a runner-exit observation race. These were verifier failures,
not passing checks or test failures. Local directories `20261101T132431567Z`,
`20261101T132510544Z`, `20261101T132533686Z`, `20261101T132556113Z`, and
`20261101T132625593Z` remain. They executed no test bodies. The monitor was corrected
to retain process handles, classify `testhost.exe` by name, and wait for an explicit
exit observation. Session `20261101T132637651Z` first passed 15 tests; final session
above repeated the focused harness after adding byte-copy and per-method count
checks. These are distinct successful runs, not 30 unique passing tests.

### PR acceptance checklist and disposition

- [x] Positive, exact-method allowlist executed: 15 passed, none failed/skipped.
- [x] Protected native/digest/token-writing tests absent from the dedicated assembly.
- [x] JSON-specific behavior demonstrated with identical assembly binaries.
- [x] Testhost identities, parents, OS start/exit times and final absence recorded.
- [x] Historical 22-pass result retained with its SHA-256 scope deviation.
- [ ] OS process-start/stop event trace and direct JSON file-open trace unavailable.
- [ ] Individual parallelism-key precedence and full toolchain trust not established.
- [ ] Evidence is from a dedicated assembly, not a rerun of the original assembly.
- [ ] Independent reviewer/owner acceptance pending; no automatic Gate B acceptance.

Recommendation: the scoped behavioral JSON and observed testhost-lifetime gaps now
have reproducible evidence. Submit for owner acceptance with the tracing,
precedence and dedicated-assembly limitations above; do not claim broader security
isolation. GitHub reported PR #30 as `draft=false` during this task despite its body
saying it stayed draft. This task did not change that readiness state or merge it.
No Gate E authorization, native execution or production connectivity follows.
