# Gate E disposable VM preparation — no native execution authorized

Native source baseline: PR #33, `0405d77331a378ea63c5cea5f5fcdc8b1b6ac4a3`.
The recorded Release baseline is 1,184 passed; this preparation does not repeat
managed security implementation or Gate B investigations. The harness and all
production holds remain unchanged. No VM/account/ACL has been provisioned here.

## Concise owner checklist

- [ ] Identify a disposable **x64 Windows 11** VM/image and clean checkpoint, with
  no real application/user data, production accounts, enrolled device or secrets.
  Record hypervisor/version, VM ID, image SHA-256 and checkpoint ID. Do not use the
  development host. Keep a separate recovery snapshot outside the acceptance run.
- [ ] Minimum: two virtual processors, 4 GiB fixed RAM, >=64 GiB virtual system
  disk, Generation-2/UEFI with Secure Boot and vTPM 2.0 enabled. Use a vendor-supported host/image.
  This follows [Microsoft's Windows 11 VM requirements](https://learn.microsoft.com/en-us/windows/whats-new/windows-11-requirements).
  Previous measured guest OS build was 26200; bind the actual OS edition/build/
  update level instead of assuming a different image was validated.
- [ ] Use fixed local **NTFS** for source, artifacts and test-account LocalAppData;
  reserve >=8 GiB free for this workflow. No UNC, mapped/shared/removable drive,
  cloud redirection, reparse ancestry or host filesystem passthrough.
- [ ] Custodian supplies an existing **standard local nonadministrator account**
  in a workgroup, with a disposable profile and no production login. Record SID
  and actual LocalAppData, not a password. Re-logon after any authorized membership
  change. No account, password, Windows Hello or credential provisioning is
  authorized by this task. If the image lacks that account, stop for separate image
  preparation authorization; do not bypass Windows setup or change account groups.
- [ ] Guest x64 .NET 10 runtime and PowerShell >=7.4 are present. Pin exact runtime
  and OS updates in the packet. Previous measured host runtime was 10.0.13 and
  builder SDK 10.0.402 (`global.json`); those are historical observations, not a
  recommendation to ignore security updates. SDK is needed only on the builder.
- [ ] Disconnect all guest network adapters at the hypervisor. Disable shared
  folders, clipboard/drive redirection/enhanced-session sharing, USB/HID/device
  passthrough and host credential integration. No G HUB/phone/device is attached.
  Record host-side evidence; an inactive guest NIC alone cannot prove isolation.
  Keep a documented stable clock/time-source, with no clock changes during a run.
- [ ] Supply an existing owner-only private artifact tree (including its nested
  runtime directories) and `<LocalAppData>\PBC.GateE.Acceptance` parent. **Do not
  create the run root.** The private-tree DACL/ownership contract is the existing
  harness contract. If missing or unsuitable, report the exact gap; no `icacls`,
  `Set-Acl`, ownership takeover or other ACL repair is authorized here.
- [ ] Stage only the reviewed eleven files below and a manually bound manifest;
  keep preflight scripts, source and approval packet OUTSIDE that artifact tree.
  Establish source/build provenance and independent hashes, then run read-only
  preflight under the selected account. Any FAIL/MISSING/NOT_OBSERVED blocks the
  packet; owner attestations remain required even when observations match.
- [ ] Submit the [approval packet](gate-e-admission-approval-packet.md) for a
  **source-only, harness-only admission revision**. That review does not authorize
  native execution. No acceptance executable is run during preparation.

## Reproducible image and artifact recipe

Image/account/runtime installation and private-folder ACL setup are custodian
prerequisites, not actions performed or authorized by this document. Freeze an
approved golden-image hash/checkpoint after those separately approved prerequisites.
Record changes from that image; do not silently install packages, update Windows,
change ACLs, join a domain or alter integration settings after evidence capture.

Build on a separate clean reviewed source checkout; do not switch/reset this
existing working checkout. For 0405d77, use that exact full commit. For a later
admission revision, rebuild and rebind EVERYTHING to that new reviewed commit.
Record full commit, clean build scopes, SDK/runtime/OS, restore lock files and build
log. A locally matching hash proves byte identity, not who built the file or that
its source was reviewed. Retain owner-approved builder provenance independently.
Do not rerun full regression or Gate B simply to prepare this VM.

Build recipe for the custodian's separately reviewed builder, **not part of the
read-only preflight and not executed in this preparation**:

```powershell
git --no-optional-locks -C '<clean-reviewed-source>' rev-parse HEAD
dotnet restore '<clean-reviewed-source>\tools\GateE.NativeAcceptance\GateE.NativeAcceptance.csproj' --locked-mode
dotnet build '<clean-reviewed-source>\tools\GateE.NativeAcceptance\GateE.NativeAcceptance.csproj' -c Release --no-restore
```

Use the existing build output for that exact source and toolchain. Transfer a
selective copy via a reviewed offline medium, then detach it. Staging writes new
guest files only into already approved private directories; it must not overwrite
another run, modify ACLs or launch anything. If paths/permissions are not ready,
stop rather than supplying an automatic staging/remediation script.

| Required artifact relative to private stage | Verification |
| --- | --- |
| `GateE.NativeAcceptance.exe` | SHA-256, <=64 MiB |
| `GateE.NativeAcceptance.dll` | SHA-256, <=64 MiB |
| `GateE.NativeAcceptance.deps.json` | SHA-256, <=64 MiB |
| `GateE.NativeAcceptance.runtimeconfig.json` | SHA-256, <=64 MiB |
| `PeripheralBatteryCompanion.Core.dll` | SHA-256, <=64 MiB |
| `PeripheralBatteryCompanion.HyperXHelperProtocol.dll` | SHA-256, <=64 MiB |
| `Microsoft.Data.Sqlite.dll` | SHA-256, <=64 MiB |
| `SQLitePCLRaw.batteries_v2.dll` | SHA-256, <=64 MiB |
| `SQLitePCLRaw.core.dll` | SHA-256, <=64 MiB |
| `SQLitePCLRaw.provider.e_sqlite3.dll` | SHA-256, <=64 MiB |
| `runtimes/win-x64/native/e_sqlite3.dll` | SHA-256, <=64 MiB |

The inherited SQLite dependencies are pinned, not invoked; do not stage any real
database. No other executable file or runtime platform tree is accepted. Only the
three existing known PDB names may additionally be present. Keep the relative
native directory structure; do not copy the unrestricted multi-platform output.

## Binding and read-only preflight

Complete a COPY of `execution-manifest.template.json` as `execution-manifest.json`
adjacent to the staged apphost. Bind the selected SID, LocalAppData, apphost path,
full reviewed source commit, eleven independently recorded hashes and whole-UTC
times. Run window <=1 hour; certificate validity <=24 hours, enclosing the run.
Keep run/identity UUIDs unique and unused. Do not advance a stale time window
without a newly reviewed packet. Do not create a fixture key to check its name.

Record the completed manifest SHA-256 separately in the approval packet; hashing
the manifest locally is not its approval. Do not embed a binary's own hash inside
that binary or require circular source/binary hashes. The admission revision will
need a new reviewed source/build/manifest binding without becoming a generic
manifest-as-permit mechanism.

Run this command ONLY inside the selected prepared VM, outside the stage tree:

```powershell
pwsh -NoLogo -NoProfile -NonInteractive -File '<reviewed-preflight-source>\tools\GateE.NativeAcceptance\preflight.ps1' `
  -VmId '<owner-recorded-VM-ID>' -SnapshotId '<clean-checkpoint-ID>' `
  -SourceRoot '<exact-reviewed-source-checkout>' `
  -ArtifactDirectory '<existing-private-stage>' `
  -ManifestPath '<existing-private-stage>\execution-manifest.json' `
  -ExpectedSourceCommit '<full-reviewed-40-hex-commit>' `
  -ExpectedManifestSha256 '<independently-reviewed-64-hex-manifest-hash>' `
  -ExpectedRuntimeVersion '<exact-approved-runtime-version>'
```

It emits JSON to stdout only: observed bindings, safe failure codes, missing
inputs, NOT_OBSERVED checks and remaining owner decisions. It never writes an
evidence/manifest file, changes ACLs, repairs anything, probes CNG, loads assemblies,
launches the harness/tests or makes a network request. Git is used only for local
read-only revision/index checks with optional locks disabled. OS, account, ACL,
NIC, process and runtime-directory observations are read-only metadata.
If script policy blocks execution, stop for approved script delivery/signing;
do not use ExecutionPolicy Bypass or change policy as a preparation shortcut.

It does not pin handles or prove absence of hardlinks, physical VM isolation,
image provenance, standard-account membership after logon, runtime binary trust,
key-name freshness, directory flushing, Job containment or crypto/crash behavior.
Those are owner attestations or protected future checks, not inferred successes.
Actual fixture key absence is checked only in an authorized native case.

No selected VM/manifest was supplied for this preparation. Live VM preflight is
**NOT RUN**; missing-parameter mode deliberately performs no host inspection.
Policy/AST tests are separate and do not validate the environment.
