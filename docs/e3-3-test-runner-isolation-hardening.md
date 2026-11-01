# E3.3 — W1Offline test-runner isolation hardening

**Status: SOURCE PATCH PREPARED; runtime behavior NOT VERIFIED.**
**Gate B: NOT AUTHORIZED. Gate E: BLOCKED.**

## Scope

This change addresses two source-configuration omissions in the isolated W1Offline test project. It does not authorize restore, build, test discovery, test execution, or native lifecycle execution.

Base: `feature/stage-e3-2-pure-security-policy`
Working branch: `feature/stage-e3-3-test-runner-isolation`

## Changes

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
- No native lifecycle source, authorization guard, permit logic, report logic or Gate B/E state was changed.
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

1. Independently review the diff and confirm there are no accidental project-property or package changes.
2. Define an explicitly approved, non-native verification protocol for output-copy and runner configuration. It must not select or execute the protected native test.
3. Separately review testhost process isolation, retries, environment mutation, static latch effects and runner behavior before any authorization request.
4. Reconcile any findings from Codex's independent PR review before changing this patch.

## Decision

This patch fixes the two identified project-item omissions at source level. The static directory inventory accounts for all files in the test-project directory. It does **not** establish runtime isolation or make the native test safe to run.

Keep Gate E blocked and the native test skipped. No gate transition or merge is authorized by this document.
