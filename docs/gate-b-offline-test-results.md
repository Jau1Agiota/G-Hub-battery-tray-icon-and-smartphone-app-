# Gate B — W1 offline harness verification

**Status:** Controlled offline build and test execution completed. **Gate B authorization remains NOT_AUTHORIZED. Gate E remains BLOCKED.** This record documents observed test results only; it is not a gate approval, native-execution permit, production-security validation, or connectivity authorization.

## Execution provenance

- Repository: `Jau1Agiota/G-Hub-battery-tray-icon-and-smartphone-app-`
- Local checkout branch: `feature/stage-e2-snapshot-interop`
- Local checkout HEAD: `ebca523c47aab663bff944d85b82881c131e2168`
- Test project: `tests/PeripheralBatteryCompanion.W1Offline.Tests/PeripheralBatteryCompanion.W1Offline.Tests.csproj`
- Test project SHA-256: `764FDF942F1EEA67BA4011379B6F52718607C14F78BC7F0E107D605483EA0935`
- SDK: .NET SDK `10.0.402`
- Runtime: .NET `10.0.13`, roll-forward disabled
- Target: `net10.0-windows`, x64
- Test adapter: xUnit VSTest Adapter `3.1.5`
- Package versions: Microsoft.NET.Test.Sdk `17.14.1`, xUnit `2.9.3`, xunit.runner.visualstudio `3.1.5`
- Restore source recorded in assets: `https://api.nuget.org/v3/index.json`
- All build/test invocations used `--no-restore`; no restore was run for this verification.

The test project and native lifecycle test source were also inspected on the E3.2 PR branch. Execution provenance above remains the local E2 branch and commit; this report does not claim that the E3.2 PR head itself was checked out and executed.

## Source identity recorded from the local checkout

| File | SHA-256 |
| --- | --- |
| `W1OfflineLifecycleTests.cs` | `3027D7FB3973EA6EF39804EC272CA8ACF1FE329E2D109A7B49C658E5E1809879` |
| `W1PreflightPlan.cs` | `1ED2F6C07F2EDAB01DC73799A06A0379BDFF35937B1A7209E58B647C3541EC25` |
| `W1NativeLifecycle.cs` | `6E7089D3F8472850BDAFC47AF4A31B7668B3277D89B3A4890DEA02116E11CB27` |
| `W1Evidence.cs` | `BA999F68B9A7279FCB5894D413FD6D6806F69B8A9EEA66B3C43163DE2DFD95C0` |
| `GateEAuthorizationGate.cs` | `5B6BF85C5B76E47118DB2ED7B539F1C8D5D0A83564552A60175AF2F4E001A51D` |
| `GateEAuthorizationGateTests.cs` | `E246F1ED89EDC6F1160648DF6B5171776D73395726047D6E649CDFDE6F1C211E` |

## Commands and observed results

All commands ran from the local Windows checkout. No package restore was invoked.

1. **Build-only**
   ```powershell
   dotnet build <W1Offline.Tests.csproj> --configuration Release --no-restore --no-dependencies --nologo --verbosity quiet -p:UseSharedCompilation=false -p:RunAnalyzers=false
   ```
   Result: exit code 0; build succeeded; 0 warnings, 0 errors; 6.42 seconds.

2. **Test discovery only**
   ```powershell
   dotnet test <W1Offline.Tests.csproj> --configuration Release --no-build --no-restore --list-tests --nologo --verbosity quiet
   ```
   Result: exit code 0; six tests discovered. Discovery did not execute tests.

3. **Frozen serialization/digest vector**
   Filter: `W1OfflineLifecycleTests.FrozenTestVector_CanonicalSerializationAndDigest_MatchesSpecification`.
   Result: 1 passed. Canonical digest assertion matched the frozen expected value.

4. **Authorization-gate unit tests**
   Filter: `FullyQualifiedName~PeripheralBatteryCompanion.W1Offline.Tests.GateEAuthorizationGateTests`.
   Result: 4 passed. These tests modify only the test process's `PBC_GATE_E_AUTHORIZED` environment variable and a static in-process latch, which their setup/teardown reset. They are not side-effect-free in the strict sense.

5. **Complete offline suite**
   ```powershell
   dotnet test <W1Offline.Tests.csproj> --configuration Release --no-build --no-restore --nologo --verbosity normal
   ```
   Result: exit code 0; 6 total, 5 passed, 1 skipped, 0 failed; 1.1131 seconds. Test host reported build succeeded with 0 warnings and 0 errors.

## Native lifecycle exclusion

`W1OfflineLifecycleTests.SingleShot_UnnamedNonExportableP256_PublicLeaf_NoStore` was explicitly skipped by its source attribute:

`GATE E BLOCKED: explicit owner approval and isolated execution required.`

It was not executed. The native P-256/CNG lifecycle, certificate creation, key custody, metadata inspection, cleanup, and report-write path therefore have **no test result** from this run.

## Scope and limitations

- The five passing tests cover one frozen canonical serialization/digest vector and four authorization-gate behaviors (missing environment token, empty permit payload, outside validity window, and one-shot latch).
- This is evidence that the selected offline test host could build, discover, and execute the selected tests under the recorded local environment. It is not evidence of production security or native lifecycle correctness.
- `--no-restore` prevents NuGet restore in these invocations; it is not, by itself, proof of OS-level network isolation.
- No TLS, sockets/listeners, DPAPI, Windows certificate store, Android Keystore, credential issuance, enrollment, pairing, live telemetry, or connectivity operation was authorized or performed.
- The local repository had zero tracked changes after the build. Three pre-existing untracked items were preserved: `858.min.js`, `app.min.js`, and `src/PeripheralBatteryCompanion.Windows/Properties/PublishProfiles/`.
- No merge, release, or gate transition is authorized by these results.

**Gate B:** controlled offline test results recorded; authorization remains **NOT_AUTHORIZED** pending its independent approval process.  
**Gate E:** **BLOCKED**. Native execution and production connectivity remain unvalidated.
