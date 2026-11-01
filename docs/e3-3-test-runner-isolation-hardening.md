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

## Preserved constraints

- Target framework remains `net10.0-windows`.
- Runtime framework remains `10.0.13`; roll-forward remains disabled.
- x64 target, framework-dependent execution and existing package versions remain unchanged.
- Default item discovery remains disabled.
- Existing native lifecycle test retains its explicit xUnit Skip attribute.
- No native lifecycle source, authorization guard, permit logic, report logic or Gate B/E state was changed.
- No build, restore, test discovery, test execution or native API was run as part of this source patch.

## Static acceptance

| Check | Result |
|---|---|
| Assembly-level xUnit configuration source exists | PASS — source inspection |
| AssemblyInfo.cs explicitly listed once in Compile | PASS — project file |
| Runner JSON explicitly copied to output | PASS — project file |
| Runner JSON content consistent with single-thread/no-parallel policy | PASS — source inspection |
| Existing native test Skip preserved | PASS — source unchanged |
| Runtime runner actually loads JSON | NOT VERIFIED |
| Compiled assembly contains expected assembly attribute | NOT VERIFIED |
| Testhost executes serially in practice | NOT VERIFIED |
| Package/testhost/runtime binary provenance | NOT VERIFIED |
| Build/restore/discovery/test execution | NOT RUN |

## Remaining E3.3 work

1. Inspect the full test-project directory for any other configuration or source files omitted by `EnableDefaultItems=false`; document each inclusion/exclusion decision.
2. Independently review the diff and confirm there are no accidental project-property or package changes.
3. Define an explicitly approved, non-native verification protocol for output-copy and runner configuration. It must not select or execute the protected native test.
4. Separately review testhost process isolation, retries, environment mutation, static latch effects and runner behavior before any authorization request.

## Decision

This patch fixes the two identified project-item omissions at source level. It does **not** establish runtime isolation or make the native test safe to run.

Keep Gate E blocked and the native test skipped. No gate transition or merge is authorized by this document.
