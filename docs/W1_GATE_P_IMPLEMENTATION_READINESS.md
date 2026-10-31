# W1 Gate P implementation readiness

**Recommendation: REVISE. Gate I is not ready to request.** Documentation only;
no runnable project/source/configuration or commands approved by this report.
The accepted public-leaf-only direction and test-only ACL exception do not need
reacceptance. Production `NonExportable AND CurrentUserAclRestricted` is unchanged.

## Blocking evidence and next review

1. **P1: exact source/binary mapping INCONCLUSIVE.** Public `v10.0.13` resolves to
   `6aa72daa24f66c01c3d7b6a32519e9d21396deec`, but the checksum-verified official
   Windows x64 runtime ZIP `.version` reports `4099f83977f4d29604a9aeb971fb0a7bc1d5f699`.
   Public runtime commit lookup422/compare404 did not resolve it. Obtain an official
   servicing build manifest, SourceLink or matching source archive that explains
   this mapping; then close the full transitive call graph at that revision.
   Do not replace the proposed runtime or silently assume the tag matches.
2. **P2: metadata evidence needs a fail-closed design revision.** Standard CngKey
   primitive getters can map a missing property to zero, including ExportPolicy
   where zero also means None. Specify bounded, exact-length public property reads
   that distinguish missing/malformed from None, and independently review them.
   Public `GetProperty` is a candidate, not implemented or granted approval here.
3. **P3: select and lock SDK/runtime/testhost independently.** Owner inventory
   supports proposed Windows x64/net10.0-windows SDK10.0.402/runtime10.0.13, but
   neither has been selected or observed for W1. Close generated host/adapter
   artifacts and selected-binary evidence under appropriate later approvals.
4. **P4: owner execution inputs remain unset.** Exact canonical private IPv4 or
   ULA (no scope/mapping/alias ambiguity); exact synthetic subject; positive public
   serial with encoding/length; bounded UTC interval and clock-confidence rule;
   SAN/EKU/KU/basic-constraint criticalities; resource/output budgets/destination;
   no-UI/timeout/abrupt-death and CleanupUnconfirmed handling; exact consent terms.
   Proposed64KiB public aggregate/1–20byte serial/8KiB output remain proposals,
   not executable defaults. No address, time, serial, permit or operation ID invented.

## SDK/runtime selection proposal — text only

Microsoft documentation reviewed:
- https://learn.microsoft.com/en-us/dotnet/core/tools/global-json
- https://learn.microsoft.com/en-us/dotnet/core/versions/selection
- https://learn.microsoft.com/en-us/dotnet/core/project-sdk/msbuild-props

A future nested `global.json` should request SDK10.0.402, `rollForward: disable`,
`allowPrerelease: false`. Root currently requests10.0.402/latestPatch and is to stay
unchanged. CLI muxer search starts at CWD; MSBuild resolver search can start at the
solution/project directory. Require CWD inside the isolated directory and no solution
entry; a project argument from root alone does not guarantee nested selection.
Pin PATH/host architecture under review without dumping private installation paths.
SDK selection does not select the framework loaded by VSTest's child host.

For a future framework-dependent x64 net10.0-windows project, propose exact
`RuntimeFrameworkVersion`10.0.13, runtimeconfig `framework.version`10.0.13 and
`rollForward` Disable. Default runtime selection rolls to servicing patches, so
net10.0/latestPatch/default host policy is insufficient. Do not mix legacy
applyPatches settings or inherit ambient DOTNET_ROLL_FORWARD overrides unreviewed.
Actual generated runtimeconfig/deps/testhost selection and command-line/env overrides
must be independently inspected under Gate B. No WindowsDesktop framework is needed
by this isolated non-UI proposal; adding one would expand source/runtime scope.

VSTest17.14.1/testhost17.14.1 and adapter3.1.5/xunit2.9.3 are only proposed versions
matching existing declarations, not proof of a resolved compatible W1 graph.
PlatformTarget x64/RID and VSTest TargetPlatform X64 must agree; inspect resolved
host executable architecture, runtimeconfig used by child process and adapter
loading. Parent CLI runtime is not child runtime proof. Any generated config or
runner that cannot enforce exact10.0.13 is a blocker. A separately reviewed direct
host invocation with exact framework selection or isolated verified runtime layout
is an alternative design requiring approval, not an authorized fallback command.
Do not patch global installations/host configuration or run discovery to find out.

No actual runtime has been selected/loaded here. A later approved phase must provide
attributable loaded-runtime/assembly version+commit/binary hash evidence in sanitized
form, while preventing native work before the owner-approved gate. Version inventory
alone does not close this requirement. No new metadata probe is requested now.

## Future Gate I allowlist — proposal only

All files would live ONLY in the nonexistent directory
`tests/PeripheralBatteryCompanion.W1Offline.Tests/`:

- `PeripheralBatteryCompanion.W1Offline.Tests.csproj`
- `W1OfflineLifecycleTests.cs`
- `W1PreflightPlan.cs`
- `W1NativeLifecycle.cs`
- `W1Evidence.cs`
- `AssemblyInfo.cs` (only nonparallel metadata)
- `xunit.runner.json`
- `global.json`
- `NuGet.Config`

Explicit Compile/None items with default globs disabled must exclude linked/copy
legacy source and any external project reference. No solution, CI, production,
Android, fixture/Option2, package publish or executable default-adapter changes.
`packages.lock.json` and generated resolved manifests belong to separately approved
Gate B. No implementation file is created by Gate P.

Proposed exact single later native case, name only:
`PeripheralBatteryCompanion.W1Offline.Tests.W1OfflineLifecycleTests.SingleShot_UnnamedNonExportableP256_PublicLeaf_NoStore`.
No theory/MemberData/ClassData/class/collection fixture, module/static initializer,
constructor/teardown native behavior, BeforeAfterTest, background work, parallelism,
retry or automatic default provider. Native routine must be inaccessible until
all preflight facts/owner approval are independently satisfied. Zero/multiple
matched cases or unexpected assembly => stop. Filter alone is insufficient because
discovery/adapter loading can execute code before filtering.

## Gate B import/dependency/discovery hazards

Text reviewed: root Directory.Build.props (nullable/implicit usings/warnings-as-errors,
deterministic/lockfile), global.json, legacy test csproj, Windows app csproj/workflow,
and locally cached Test SDK17.14.1/TestHost17.14.1 props/targets. Cached Linux
packages are historical examples, not authenticated/resolved W1 Windows assets.
Legacy test project references apps/helpers and links EphemeralCapability native
flow/Option2 source: never reuse it as the W1 host.

Future explicit Sdk.props/targets imports must set ImportDirectoryBuildProps=false /
ImportDirectoryBuildTargets=false BEFORE props, then independently inventory ALL
ancestor package/NuGet/global/SDK configs and effective imports. Also exclude
Directory.Packages.props inheritance, build/buildTransitive analyzers/generators,
custom tasks/InitialTargets/AfterTargets and SDK/workload resolver surprises.
The ordinary Windows app has helper-copy/build/publish targets; do not reference it.
No assumption that disabling two Directory.Build imports isolates the entire graph.
The existing Windows app also explicitly targets runtime10.0.14/self-contained;
that is not the proposed W1 runtime10.0.13 and must not be copied or altered here.
Local ancestor inventory found only repository Directory.Build.props/global.json
and Windows AGENTS.md among the named config files; owner-machine/user/global
NuGet/MSBuild state and the future nested project remain unaudited.

Test SDK targets set OutputType Exe and InitialTargets GenerateProgramFile, adding
an entry source; TestHost targets emit RuntimeHostConfigurationOption for parent
process query. Adapter/package code is executable at discovery/load time. Audit
all resolved package hashes/signatures/provenance/transitives, assets/lock/configs,
compiler/SDK/native-helper targets and generated source BEFORE any relevant phase.
Restore/build may execute MSBuild tasks; discovery is its own code-execution risk.
Gate B must state exactly which of restore/build/discovery is permitted, not imply
all three. Gate E requires independently reviewed source/binary/runtime/import
hashes and exact single command/selector/context; none exists or is approved now.

D3.3A incident remains explicit:11 native certificate-generation cases and two
real-secret sample cases ran outside authorization. Never run unfiltered test.ps1,
solution tests, fixture previews, old negative-only filters, Option2 or old key/
journal inspection/cleanup. No need to rerun a baseline for this docs-only gate.

## CI and publication

Existing Windows workflow triggers feature pushes/PRs and its ordinary job runs
unfiltered test.ps1 plus publish, except for the exact D3.3A branch. This new branch
would take the ordinary path. Documentation commits MUST include `[skip ci]`.
Do not modify workflow to claim safety. Inspect Actions by exact published head
SHA after push/PR; if any unexpected workflow starts, report and STOP. Android CI
must be inspected similarly. Skipped CI is not verification of runtime correctness.

Only four new documentation files are allowed (three Windows, one Android). Preserve
all source/assets/recovery exports, old reports, untracked owner work and branches.
Normal HTTPS source/git/PR traffic is permitted; it is not application TLS proof.

**NATIVE NOT RUN. TESTS NOT RUN. LAN NOT RUN. PRODUCTION SECURITY NOT PROVEN.**

Stop for owner review. No automatic Gate I implementation or Gate B/E execution,
merge, release, Android activity or production security policy change.
