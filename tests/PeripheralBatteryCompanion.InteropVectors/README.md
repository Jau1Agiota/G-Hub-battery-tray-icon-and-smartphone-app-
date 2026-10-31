# E2 portable snapshot interoperability producer — uncommitted review work

Controlled in-memory model inputs only. The wire snapshots deliberately have
synthetic=false so they exercise the actual publication path; they are NOT live
peripheral telemetry, authenticated PC evidence, or security approval.

This standalone net10.0 test executable references the real Core project. It calls:
BatterySnapshotProjection.Build -> PublishedSnapshotStore.Publish ->
BatterySnapshotJson.Serialize, then writes PublishedSnapshot.Json bytes unchanged.
No HTTP core, provider, scheduler, WebSocket, HID helper, Windows executable, native
security fixture, key/certificate operation, or authorization guard is invoked.
It is not registered in the broad solution/test.ps1: explicit portable execution
avoids accidentally building/running unrelated native/security experiments.

## Run only this project

Set DOTNET_GENERATE_ASPNET_CERTIFICATE=false BEFORE the first SDK invocation and
DOTNET_CLI_TELEMETRY_OPTOUT=1. On PowerShell use:

```powershell
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
dotnet restore tests/PeripheralBatteryCompanion.InteropVectors/PeripheralBatteryCompanion.InteropVectors.csproj --locked-mode
dotnet build tests/PeripheralBatteryCompanion.InteropVectors/PeripheralBatteryCompanion.InteropVectors.csproj -c Release --no-restore
dotnet run --project tests/PeripheralBatteryCompanion.InteropVectors/PeripheralBatteryCompanion.InteropVectors.csproj -c Release --no-build --no-restore -- --output <new-output-directory>
```

The output directory MUST NOT exist; existing files are never overwritten. Root
SDK policy is 10.0.402 with latestPatch; this task used verified SDK 10.0.403.
The new lockfile records inherited Core dependencies, not new production packages.
Do not substitute a solution-wide build or an unfiltered native test invocation.

Output: 14 exact Windows-emitted publication JSON files, 11 rejected mutations,
2 explicitly recorded reader-policy differences, a semantic manifest, and checksums
(29 files total). Mutations are labeled separately, never claimed as valid producer
output. The invalid UTF-8 and oversized files must remain exact byte data.

84 portable assertions check projection mappings, immutable/repeated reads,
serializer/store byte consistency, strict Windows reader acceptance, revision
advancement, new publisher scope and all mutation outcomes. Dates, IDs and model
values are deterministic; the clock is injected, never read from hardware or UTC now.
Two independent runs were byte-identical; the Android regeneration checker rebuilds
this project and verifies all output against its test resources without rewriting.

## Cross-repository review

Windows base: ac78aa3eccc7c16c870d0d09f565f631f4a04dd5. Production sources and guards
are unchanged. Android base: 83b83c717690cee9fb07e0c1079942785a7d707f.
Both repositories use local feature/stage-e2-snapshot-interop branches. No commit,
push, PR, merge, listener, pairing or native security experiment is authorized here.

Android's tools/verify-e2-vectors.py takes --windows-root and optional --dotnet,
regenerates to a temporary directory with --no-restore, compares every byte, and
leaves vendored resources unchanged. Its tests parse exact output and independently
assert the device/timestamp/cache/sequence semantics. See Android's
 docs/stage-e2-snapshot-interop.md for results and separately proposed policy fixes.

## Execution evidence and limitation

Portable Release build: PASS, 0 warnings/errors. Producer: 14 publications,
13 controlled mutations, 84 assertions PASS. Android: 223 JVM tests PASS, no
failures/errors/skips; debug/release builds and lint PASS (0 errors, 9 warnings).
Broad Windows suite, Windows-native/device/security tests and actual connectivity
were NOT RUN; no such readiness is inferred.

SDK SETUP CAVEAT: the first .NET restore reported installing a local development
HTTPS certificate through SDK initialization. This was unintended, was not used or
trusted by E2, and was not a Windows native-security experiment. Subsequent commands
explicitly disable automatic certificate generation. No certificate cleanup/trust
command was run. Do not interpret SDK setup as Gate B/E or platform-security proof.

## Corrective review follow-up

The indented manifest options explicitly set `NewLine = "\n"`. Producer assertions
verify the policy, actual LF line breaks and absence of CRLF sequences. The production
snapshot serializer is untouched. Fresh output matches all 29 existing Android
candidate files exactly; no expected bytes were adjusted.

Android now fails missing/mistyped accepted-publication expectations and restricts
policy-only handling to the two named cases with explicit assertions. The corrective
run passed 84 producer assertions and 223 Android JVM tests, plus both APK builds
and lint. Execution was on Linux; actual Windows OS execution remains UNVERIFIED
because no Windows runner is attached. Run only the portable project/verifier there
before claiming Windows execution. No native/security operation is required by it.
Both feature branches remain uncommitted/unpushed pending owner review.
