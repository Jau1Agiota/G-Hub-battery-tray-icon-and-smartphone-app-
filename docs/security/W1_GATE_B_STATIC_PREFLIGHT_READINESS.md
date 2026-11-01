# W1 Gate B static preflight: readiness and execution hold

**Status:** Static inspection complete; **Gate B execution NOT AUTHORIZED**; **Gate E BLOCKED**.  
**Evidence date:** 2026-10-31 / 2026-11-01 (operator-provided local Windows PowerShell output).  
**Scope:** Windows W1 offline CNG P-256 experiment, `tests/PeripheralBatteryCompanion.W1Offline.Tests`.  
**Evidence baseline:** local `feature/stage-e2-snapshot-interop` at `ebca523c47aab663bff944d85b82881c131e2168`. This local branch was **not** available as a GitHub remote ref when this document was published. This document is a report of local evidence, **not** a verification of the remote branch or a claim that its source exists on `main`.

## Executive decision

Static review is finished, but it does **not** authorize running restore, build, MSBuild evaluation, test discovery, tests, or native cryptography. The inspected native test has an xUnit `Skip` attribute and calls an unconditional blocking guard as its first statement. `W1NativeLifecycle.Execute` also calls unconditional approval and provenance blockers before native work. These are **source-level observations only**; no dynamic execution or compiled-binary/source provenance proof was performed.

**Gate B:** NOT AUTHORIZED.  
**Gate E:** BLOCKED.  
**Gate I:** source-only inspection.

## Local baseline and preservation

- Local branch: `feature/stage-e2-snapshot-interop`; HEAD `ebca523c47aab663bff944d85b82881c131e2168`.
- Tracked working-tree changes: **0**; staged changes: **0** at final checkpoint.
- Existing untracked items preserved: `858.min.js`, `app.min.js`, and `src/PeripheralBatteryCompanion.Windows/Properties/PublishProfiles/`.
- No restore, build, test discovery, MSBuild evaluation, native CNG operation, approval, or repository mutation was performed during the local preflight.

### Six source SHA-256 fingerprints

| Source file | SHA-256 |
|---|---|
| `W1OfflineLifecycleTests.cs` | `3027D7FB3973EA6EF39804EC272CA8ACF1FE329E2D109A7B49C658E5E1809879` |
| `W1PreflightPlan.cs` | `1ED2F6C07F2EDAB01DC73799A06A0379BDFF35937B1A7209E58B647C3541EC25` |
| `W1NativeLifecycle.cs` | `6E7089D3F8472850BDAFC47AF4A31B7668B3277D89B3A4890DEA02116E11CB27` |
| `W1Evidence.cs` | `BA999F68B9A7279FCB5894D413FD6D6806F69B8A9EEA66B3C43163DE2DFD95C0` |
| `GateEAuthorizationGate.cs` | `5B6BF85C5B76E47118DB2ED7B539F1C8D5D0A83564552A60175AF2F4E001A51D` |
| `GateEAuthorizationGateTests.cs` | `E246F1ED89EDC6F1160648DF6B5171776D73395726047D6E649CDFDE6F1C211E` |

## Project and dependency review

The W1 project declares `net10.0-windows`, runtime `10.0.13`, disabled runtime roll-forward, `x64`, non-self-contained execution, no apphost, deterministic compilation, and explicit compile items. It disables directory-level build props, targets, and central package props before SDK imports. Consequently, the root `Directory.Build.props` setting `RestorePackagesWithLockFile=true` is not inherited by this project; no W1 `packages.lock.json` was observed.

Direct package references: `Microsoft.NET.Test.Sdk 17.14.1`, `xunit 2.9.3`, `xunit.runner.visualstudio 3.1.5`. Thirteen resolved packages were inventoried. Archive SHA-512 matched the adjacent `.nupkg.sha512` files; assets-file hashes matched `.nupkg.metadata.contentHash`. These two hash categories differed from each other. Their internal consistency is **not independent publisher-authenticity verification**. Generated NuGet props/targets and test SDK targets were statically inspected.

Installed SDK `10.0.402` and runtime `10.0.13` were examined, including SDK imports, workload resolver, 14 workload-manifest families, extension points, and environment overrides. The manifest imports for Android, Apple, Emscripten, WASM/WASI, and Mono AOT are generally conditional on platform or workload settings. The MAUI manifest requires `UseMaui*` properties; none were declared in the W1 project or inspected generated imports, and relevant process environment overrides were unset. **This is not a substitute for MSBuild evaluation.** The absence of literal workload locator directories is expected for virtual SDK resolver names, not proof of a broken SDK.

Existing `bin/Release/net10.0-windows` artifacts were inventoried but were **not** established as reproducible from the inspected source. Test host/adapter initialization and runtime behavior remain unverified.

## Security and execution findings

1. **Native hold:** `SingleShot_UnnamedNonExportableP256_PublicLeaf_NoStore` is marked `[Fact(Skip = ...)]` and first calls `RequireExplicitNativeApproval()`, which unconditionally throws. `W1NativeLifecycle.Execute()` first calls that blocker and then `RequireVerifiedExecutionProvenance()`, also unconditionally throwing. No native execution occurred.
2. **Permit authorization gap:** `GateEAuthorizationGate.EnsureAuthorized()` accepts a nonempty permit payload string with a process environment token and process-local one-shot latch; it does **not itself** authenticate the permit cryptographically. The separate `GateEPermit.TryAcquireFromFile()` checks a signed permit and plan binding but always fails closed pending durable nonce reservation.
3. **Replay gap:** the process-local latch is resettable for tests and is not a durable cross-process replay ledger. Trusted nonce reservation and provenance binding remain unresolved.
4. **Report write hold:** `RequireVerifiedReportDestination()` throws before the `FileStream(FileMode.CreateNew)` report write. Destination verification remains unimplemented.
5. **Test-side effects:** four authorization tests modify `PBC_GATE_E_AUTHORIZED` in the test-host process and reset a static latch via test setup/teardown. They are **not side-effect-free** even though no native operation is expected from those tests.
6. **Potential native work behind blockers:** the guarded body contains unnamed `CngKey.Create(ECDsaP256)`, signing-use configuration, public-only certificate work, metadata observation, and cleanup attempts. Static code cannot prove provider allocation behavior or cleanup success.
7. **Contract alignment:** `TryTestIp` accepts RFC1918 IPv4 and IPv6 ULA; the proposed E3.1 endpoint identity contract is exact RFC1918 IPv4. This discrepancy requires explicit reconciliation before any broader integration.

## Proposed next gates (proposal only; no authorization)

1. Preserve the above baseline and untracked items. Verify a reviewed source revision and its remote provenance before any execution.
2. Obtain **separate, explicit owner approval** for a narrowly scoped **Gate B** procedure. Specify exact project/ref, SDK, offline/network policy, command(s), outputs, and rollback boundaries. Review any restore/build-generated file writes and test-host side effects first.
3. If approved, perform only the explicitly permitted Gate B steps, recording exit codes, changed files, and hashes. Test discovery is a separate operation and should not be silently bundled with a build.
4. Keep the native xUnit test skipped and all unconditional native guards in place. Do not set `PBC_GATE_E_AUTHORIZED` as a shortcut.
5. Before considering **Gate E**, require independently reviewed cryptographic permit validation, durable atomic nonce reservation, execution provenance, trusted report destination, exact endpoint-identity alignment, and a separate owner-approved isolated execution plan.

## Evidence limitations

This document is compiled from user-supplied read-only local PowerShell outputs and static source excerpts. It does not claim an independent inspection of the owner's local machine, proof of package publisher authenticity, evaluated MSBuild import graph, runtime isolation, binary provenance, or safe native key lifecycle. The GitHub documentation commit is **documentation-only** and must not be construed as Gate B or Gate E approval.
