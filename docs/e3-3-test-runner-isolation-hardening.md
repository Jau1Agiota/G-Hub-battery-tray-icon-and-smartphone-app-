# E3.3 — W1Offline test-runner isolation hardening

**Status: SOURCE PATCH PREPARED; runtime behavior NOT VERIFIED.**
**Gate B: NOT AUTHORIZED. Gate E: BLOCKED.**

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
