# W1-PREP approval requests — no implementation or native run approved

[Implementation specification](W1_PREP_ISOLATED_IMPLEMENTATION_SPEC.txt) ·
[Static evidence](W1_PREP_STATIC_EVIDENCE.txt)

The owner has accepted the public-leaf-only direction and test-only **ACL evidence
NOT ESTABLISHED** exception **for preparation**. That decision is retained. It does
not approve runnable source, builds/discovery, native execution or production use.

## Current request: owner-only read-only platform inspection

**Specific question:** Will you run the single block below in an ordinary,
non-elevated Windows PowerShell session on the intended PC and share its sanitized
output? It is metadata inspection only, not a request to run W1. **Codex did not
execute it.** Do not run any other test, script, `certutil`, provider probe or build.

The block reads local OS/profile/file metadata and asks the PATH-selected .NET
host only for installed version lists. It prints no usernames, SIDs, host names,
install paths or raw error details. It does not open CNG/KSP, keys, certificate
stores, DPAPI, networking or applications. Reading the DLL's version is not provider
activation. Profile/context checks use OS token/CIM metadata, not crypto. Review
before running; no administrator rights or execution-policy change are required.

```powershell
& {
    $facts = [ordered]@{
        WindowsEdition = 'UNKNOWN'; WindowsVersion = 'UNKNOWN'; WindowsBuild = 'UNKNOWN'
        OSArchitecture = 'UNKNOWN'; ProcessArchitecture = 'UNKNOWN'
        Interactive = [Environment]::UserInteractive; ElevatedAdmin = 'UNKNOWN'
        CurrentProfileLoaded = 'UNKNOWN'; InstalledSDKs = @(); InstalledRuntimes = @()
        DotnetInventory = 'UNKNOWN'; KspDllPresent = 'UNKNOWN'; KspDllVersion = 'UNKNOWN'
        ActualKspContext = 'UNKNOWN - provider not opened'
        W1SelectedSDK = 'NOT SELECTED'; W1SelectedRuntime = 'NOT SELECTED'
    }
    try {
        $os = Get-CimInstance -ClassName Win32_OperatingSystem -ErrorAction Stop
        $cv = Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' -ErrorAction Stop
        $facts.WindowsEdition = [string]$os.Caption
        $facts.WindowsVersion = [string]$os.Version
        $facts.WindowsBuild = if ($null -ne $cv.UBR) { '{0}.{1}' -f $os.BuildNumber, $cv.UBR } else { [string]$os.BuildNumber }
    } catch { }
    try {
        $facts.OSArchitecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
        $facts.ProcessArchitecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
    } catch { }
    $identity = $null
    try {
        $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
        $principal = [Security.Principal.WindowsPrincipal]::new($identity)
        $facts.ElevatedAdmin = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
        $profile = Get-CimInstance -ClassName Win32_UserProfile -Filter ("SID='{0}'" -f $identity.User.Value) -ErrorAction Stop
        if ($null -ne $profile) { $facts.CurrentProfileLoaded = [bool]$profile.Loaded }
    } catch { } finally { if ($null -ne $identity) { $identity.Dispose() } }
    try {
        $dotnetHost = Get-Command dotnet -CommandType Application -ErrorAction Stop | Select-Object -First 1
        $sdkLines = @(& $dotnetHost.Source --list-sdks 2>$null)
        $sdkExit = $LASTEXITCODE
        $runtimeLines = @(& $dotnetHost.Source --list-runtimes 2>$null)
        $runtimeExit = $LASTEXITCODE
        if ($sdkExit -eq 0 -and $runtimeExit -eq 0) {
            $facts.InstalledSDKs = @($sdkLines | ForEach-Object {
                if ($_ -match '^([0-9]+\.[0-9]+\.[0-9]+(?:[-+][^\s]+)?)\s+\[') { $matches[1] }
            })
            $facts.InstalledRuntimes = @($runtimeLines | ForEach-Object {
                if ($_ -match '^(Microsoft\.[A-Za-z0-9.]+)\s+([0-9]+\.[0-9]+\.[0-9]+(?:[-+][^\s]+)?)\s+\[') { '{0} {1}' -f $matches[1], $matches[2] }
            })
            $facts.DotnetInventory = 'PATH host lists only; selected W1/testhost runtime not established'
        }
    } catch { }
    try {
        $module = Get-Item -LiteralPath (Join-Path $env:SystemRoot 'System32\ncryptprov.dll') -ErrorAction Stop
        $facts.KspDllPresent = $true
        $facts.KspDllVersion = [string]$module.VersionInfo.FileVersion
    } catch { }
    [pscustomobject]$facts | ConvertTo-Json -Depth 3
}
```

This is the **only current owner inspection command** supplied. UNKNOWN values
remain blockers; do not automatically install anything, elevate or retry probes.
File version/presence does not prove KSP registration, loaded module, policy/UI
behavior or actual provider context. A 32-bit/emulated host may report a different
inventory or system-file view; that must be resolved before selecting the W1 target.
Installed SDK/runtime lists do not identify what the future adapter/testhost loads.
No user identity or machine-local path needs to be shared. If output contains any
unexpected personal detail, redact it before sharing. No crypto output is requested.

## Gate P — platform and static servicing reconciliation

Pending owner metadata and exact SDK/runtime selection. Independently compare
the actual servicing source with S0 commit
`60629d14374c56f1cb51819049ad1fa529307f8d`. Resolve creation/property setters,
wrapper duplicate, public generator/Create, implicit factories and disposal helpers.
Record changed/unchanged/unknown with exact commits/lines; do not assume source
agreement proves runtime behavior. If selected runtime cannot be pinned/audited,
**STOP**. No native implementation follows automatically from inspection output.

## Gate I — separate implementation-only approval request (pending)

Proposed request, **not granted**: implement only the reviewed isolated project
and exact source/manifest files under
`tests/PeripheralBatteryCompanion.W1Offline.Tests/`, outside the solution/CI and
with no legacy/production references. The file allowlist, direct/transitive
dependencies, import isolation and runtime pinning must be reviewed first.
Proposed file names are explicitly listed in specification section4; none exists
now. Future commands must fix the working directory to this isolated project so
its reviewed local SDK pin is selected; a project path alone does not ensure that.
No new crypto operations in discovery/constructors/module initializers/teardown;
native work only inside the single method after explicit one-shot owner gate.

Gate I alone permits neither native execution nor restore/build/test discovery.
Do not add a `Program`, script, CI hook, environment auto-start or native default
adapter. Production `NonExportable AND CurrentUserAclRestricted` stays unchanged.
Owner must give explicit Gate I approval after Gate P; there is none now.

## Gate B — separate build / dependency / discovery request (pending)

Builds execute SDK/imported package targets; discovery can execute fixture code.
Before approving any command, audit all sources/imports/generators/adapter setup
and specify permitted filesystem/dependency-network effects. Direct package
proposal: TestSdk17.14.1, xunit2.9.3, VS runner3.1.5. Actual resolved imports/
transitives and hashes are **UNKNOWN**, not borrowed from the old test project.
A first dependency resolution/lock-file generation needs its own reviewed command;
subsequent locked restore is not a substitute for that review.

Future command grammar only (not a runnable or approved command):
`dotnet <approved restore-or-build verb> <exact isolated project path> <reviewed lock/config/runtime options>`
No placeholders may be silently filled or run. Discovery is optional and needs
explicit permission and a pre-discovery call-graph audit; no `--list-tests` now.
No solution-level command, legacy test runner or publish/install operation.

Before requesting Gate E, reviewer records exact:

- Source commit and diff/file allowlist; reviewed SDK/runtime/source mapping.
- Project, dependency lock, resolved import/configuration manifests and SHA256s.
- Test assembly, runtimeconfig/deps files, testhost/adapter and required dependency
  hashes; no source/binary/hash values exist yet and none is fabricated here.
- Exact native method FQN, expected case count **one**, nonparallel/no-retry setup,
  approved build configuration/architecture and exact process invocation.
- Constructor/static/fixture/data/discovery/teardown call graph; no legacy assemblies
  loaded. Hash checks cannot prove code harmless; independent source review required.

## Gate E — separate exactly-one native execution request (blocked)

Not issuable yet: Gate P/I/B and independent review are incomplete, binaries do not
exist, exact commands/hashes/platform/IP/validity/serial/budgets remain unset.
Owner's eventual approval must name ONE invocation of:
`PeripheralBatteryCompanion.W1Offline.Tests.W1OfflineLifecycleTests.SingleShot_UnnamedNonExportableP256_PublicLeaf_NoStore`.
Project/method are specifications, not existing runnable files.

Future execution grammar ONLY, not an approval:
`dotnet test <reviewed isolated project or assembly> --no-build --no-restore --filter "FullyQualifiedName=<exact single approved FQN>" <approved runtime, logger and bounded-results options>`
Gate E must replace every placeholder, bind exact binary/source hashes, approve
the precise command and specify local consent mechanism, time window and one-use
gate before provider open. No real permit/token/operation ID is generated now.
No broad selector, extra fixture, native comparison key, private export, retry,
parallel execution or subsequent process. New process requires owner review.

Exact future approved-native list is limited to the reviewed explicit null-name
None-policy creation, existing-key wrapper, public parameter/SPKI/hash reads,
one signature/public leaf creation/load, approved field comparisons and owned
disposal. No extra `LoadCertificate`, private getter/association, chain/store,
DPAPI, Keystore, TLS, socket, credential, old artifact or device operation.

Owner must explicitly accept evidence limits: ACL **NOT ESTABLISHED**; export
denial **NOT TESTED**; request/readback not causal policy proof; public leaf is not
TLS identity; managed Dispose not independent OS cleanup/nonpersistence proof.

## Stop / output / cleanup conditions for a future approved run

- Invalid or uncertain platform/consent/time/IP/limit/hash/case count → no provider
  open, **NOT RUN**, no repair/fallback.
- Unexpected UI, named/store state, extra key/import/private-export route, unknown
  ownership or disposal → **STOP**, owner review, no rerun or speculative deletion.
- Dispose only independently owned returned leaf, signer duplicate, original key;
  once each in that order. Ambiguous outcome stays `CleanupUnconfirmed`.
- Output: reviewed finite phase/status/HRESULT fields and bounded public metadata,
  no raw exception/stack, key/cert blob, identity, SID, handle or user path. Proposed
  limits (not approved): public aggregate64KiB, serial1–20 bytes, shareable result8KiB.
  TRX/adapter output and results location also require review.
- No timeout/kill/UI-cancel guarantee; any watchdog or crash injection is new scope.
- No old key/journal inspection, prefix cleanup, retired/preview ID reuse or global
  replay-proof claim. No automatic next experiment, production integration or release.

The D3.3A **11 certificate + two secret-sample** incident remains in the record.
Never run unfiltered old `dotnet test`/`test.ps1`, preview or Option2 to validate
these gates. Negative exclusion lists are not safe W1 allowlists.

**Current request is ONLY the owner metadata inspection. Gate I/B/E remain pending.**

**NATIVE NOT RUN; TESTS NOT RUN; LAN NOT RUN; PRODUCTION SECURITY NOT PROVEN.**
