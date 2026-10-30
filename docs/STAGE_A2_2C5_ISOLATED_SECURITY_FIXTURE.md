# A2.2C5 — isolated Windows security proof harness

## Decision / baseline

Implemented for owner review and explicit Windows execution. **No native proof was
executed in Codex.** This Linux environment compiles the Windows harness but cannot
validate CNG, store association, ACLs or native cleanup. Do not infer PASS from build.

Base: PR #12, `80b8550d284a59cebbe78e12a22ec04814cc5dd1`; branch
`feature/stage-a2-2c5-isolated-security-harness`, draft stacked on #12. No merge,
rebase, release or production deployment. AGENTS.md, C2/C3/C4 reports, existing test
scripts, native helpers and startup/lifecycle reviewed. No conflict/user work found.
Owner Windows SDK 10.0.402 results: targeted 10 read-only cases PASS; locked restore,
Release build, full 660 tests PASS, zero failed/skipped. Those owner results do not
include this new mutation harness or native export/ACL proof.

## Inventory / no startup registration

- `tools/PeripheralBatteryCompanion.SecurityFixture/`: separate net10.0-windows
  console executable, project/locked file, native harness and pure consent/ownership
  guards. References existing Core for offline public-field certificate checks;
  inherits its existing dependencies, no new NuGet package/version introduced.
- `scripts/Show-A22C5FixtureScope.ps1`: pure non-mutating preview. Creates only an
  in-memory random v4 GUID; prints exact proposed key/SAN and scope. Does not build,
  run the harness, enumerate resources or write files.
- `scripts/Invoke-A22C5Fixture.ps1`: distinct exact confirmation phrase plus operation
  ID required. Locked restore/build of tool then explicit execution; exits on failure.
  Does not run full regression or retry. No admin prompt/elevation.
- Two new portable guard tests, linked directly to the same pure guard source. Test
  exact consent/operation rejection, wrong key name/SPKI/journal and unrelated leaf
  selection. They cannot execute the native fixture. All prior assertions preserved.
- Solution adds the tool for compilation only; portable test project links pure
  guards. No production project, startup, tray, battery polling, G HUB, SQLite/HID,
  snapshot-v1, Android or recovery export modified. Standard test.ps1 discovers only
  existing test projects; neither building nor testing invokes fixture Main/Run.

## Exact mutation scope

Only after explicit execution:

1. Require unelevated current owner token; reject elevated administrator process.
2. Operation ID must be nonzero canonical-length GUID with v4/variant shape (preview
   generates cryptographically random Guid.NewGuid). No selecting a real key name.
3. Exact key `PBC-A22C5-Fixture-<operation>` in CurrentUser Microsoft Software KSP,
   ECDSA P-256, signing-only, explicitly ExportPolicy.None; never overwrite flag.
4. Exact local directory `LocalApplicationData/PBC-A22C5-Fixtures/<operation>`.
   Reject existing file/directory/key and reparse ancestors. Native CreateDirectoryW
   atomically refuses an existing operation directory, applying current-user-only
   protected DACL at creation. Read back owner/DACL before creating key. Root namespace
   may be created; it is never broadly deleted. File `ownership.jsonl` uses CreateNew,
   exclusive handle and WriteThrough; flush-to-disk per public-identity journal entry.
5. One self-signed **untrusted 1-hour fixture** leaf in CurrentUser/My, offline SAN
   `fixture-<operation>.invalid`, DigitalSignature, serverAuth, non-CA. Record planned
   exact DER/thumbprint before insertion; refuse preexisting selection. This is NOT
   the proposed 90-day production identity, TLS trust or validated Android authority.
6. No Root/LocalMachine install, PFX export/files, raw bearer tokens, user accounts,
   DPAPI, network/profile/firewall/router changes, listener, enrollment or Android.

Journal records version/operation/provider/scope/name, public SPKI/leaf DER and
thumbprint, stages and cleanup. No raw private key or credential persisted. It is
kept locally after successful cleanup for evidence; do not share it wholesale.
Console output contains anonymous operation ID, fixed check labels and ACL roles/
rights/inheritance/control flags; no SID, user path, key inventory or exception text.
Failure emits only exception category/HRESULT for safe diagnosis. Local build output
can contain user paths; share sanitized fixture summary, not full build logs.

## Native checks and what they mean

Each executed assertion emits PASS/FAIL. Checks not reached emit NOT RUN. Native
operations throwing before an assertion cause overall FAIL; unavailable evidence is
never interpreted as success. Cleanup has independent success/failure and exit status.

- Reopen exact persistent software KSP name silently, verify actual provider, user
  scope, ECDSA group, size 256 and export policy None.
- In-memory raw ECC-private export must throw explicit native NTE_BAD_KEY_STATE or
  NTE_PERM; other errors halt, success fails. Zero any returned private buffer in
  finally, never write it. No PFX export is attempted and it remains NOT RUN.
- Read native `Security Descr` with explicit DACL and owner flags. Require current
  owner SID, nonempty DACL, only understood non-callback CommonAce entries and no
  allow ACE to a principal outside current user/SYSTEM/Administrators. Require a
  user allow ACE; print exact masks/qualifiers/inheritance with anonymized roles.
  Unknown ACE/principal/provider behavior fails closed; do not loosen checks to pass.
- Attempt duplicate Create with no overwrite option; require NTE_EXISTS and unchanged
  public key. This exercises actual native collision refusal without another key.
- Retrieve exact newly installed public DER/thumbprint from CurrentUser/My; require
  private association to the owned CNG name/user scope; inspect actual DER-SPKI SHA256,
  exact SAN (no CN/wildcard fallback), time, leaf usage/EKU offline.
- Sign random challenge using key retrieved from stored leaf; verify with leaf public
  key and require rejection by an unrelated in-memory public key. Challenge cleared.

ACL evidence is not a claim of protection from administrators/SYSTEM, same-user code,
malware or arbitrary principal attacks. Native descriptor ownership/allow-list checks
are evidence to review, not full effective-access or second-account denial proof.
No automatic ACL repair or privileges/account changes. Cross-user denial NOT RUN;
separate owner-reviewed account procedure is required. Same-process reopen is tested;
separate-process/session restart NOT RUN. Offline public fields are not chain/trust,
revocation or TLS-validation proof. No root trust is installed.

## Cleanup / failure / crash policy

Before removal require unchanged exclusive ownership journal, exact operation-derived
key name and actual SPKI equal to the owned key. For a leaf require both exact
thumbprint and full public DER; mismatched/duplicate selection aborts. Never remove
by friendly name, subject, prefix or wildcard. Remove only owned leaf, verify absence;
then delete exact reopened owned key and verify absence. Keep journal with cleaned
stage and print explicit status. Dispose wrappers/handles even on failure.

If creation's outcome is uncertain, manifest is incomplete/changed, identity differs
or cleanup fails: exit nonzero, print CLEANUP NOT CONFIRMED/orphan notice, STOP. No
silent adoption, backup, retry, broad deletion or automated recovery command exists.
Crash can leave key/cert/journal; keep exact operation journal for separately reviewed
manual recovery. Do not rerun same ID. Missing-key/partial orphan recovery is outside
this stage; do not delete another key to make a test pass. The untouched root/operation
folder and journal are intentional retained evidence, not a claim of zero disk writes.

Journal/file DACL reduces accidental other-user access; it is not cryptographic
ownership, anti-rollback or defense against same-user/privileged tampering. Ancestor
reparse checks are not a hostile same-user concurrent filesystem defense. Run only
in owner's local profile on a trusted machine, no shared/synced fixture directory.
No production ACL or credential security gate is opened by these fixture results.

## Windows owner — ONE action at a time

First obtain this feature branch using your normal Git workflow; review PR/source.
In ordinary, unelevated PowerShell from repository root, **preview only**:

```powershell
powershell.exe -NoProfile -File .\scripts\Show-A22C5FixtureScope.ps1
```

This writes nothing. Share its scope summary/operation ID for review. STOP here in
this chat; do not create fixtures until you approve the printed exact scope.

After explicit approval, use the exact generated command, separately. Form only
(do not paste the placeholder or invent/reuse an ID):

```powershell
.\scripts\Invoke-A22C5Fixture.ps1 -OperationId <fresh-id-from-preview> -Confirmation CREATE-ISOLATED-CURRENTUSER-FIXTURE
```

Execution policy is not consent; no ExecutionPolicy Bypass is required by this tool.
Use your established approved script policy. No elevated PowerShell. If a command
is blocked, stop and report; do not change machine policy.

Expected successful fixture run: each reached proof check PASS, exact-owned key/leaf
cleanup PASS, proof failed=0/not-run=0, RESULT PASS, exit 0. Cross-user, restart, PFX,
DPAPI, TLS/physical tests explicitly remain NOT RUN. Counts refer to fixture assertions,
not xUnit tests. If any failure/cleanup uncertainty, STOP without retry or cleanup.

After successful fixture results AND owner review, run regression separately:

```powershell
powershell.exe -NoProfile -File .\test.ps1 -Release -CopySummary
```

Expected automated total: previous owner 660 + 2 pure guards = 662, if same baseline
and environment. Actual runner totals govern; never claim this expected count ran
in Codex. Share sanitized summary and cleanup status. Tray/physical checks NOT RUN.

## Gates remaining / STOP

Windows fixture execution, native ACL/export/association/cleanup results: NOT RUN in
cloud. Separate-process restart, cross-user denial and broader ACL effective-access
proof remain open even after a successful fixture run. DPAPI protected verifier store,
rollback/tombstone resistance, revoke/rotate durability, wrong-user/corruption/atomic
interruption persistence are explicitly outside stage. No TLS server, production
identity, phone enrollment, Android connection or physical validation. Stop for owner
review; do not advance from fixture PASS to networking/deployment.

## Final cloud verification

Linux .NET SDK 10.0.403; authorized elevated .NET IPC/network through inherited proxy.
Locked solution restore PASS. Release solution build PASS, 0 warnings, 0 errors.
Portable regression **615 passed, 0 failed, 0 skipped** (613 existing + 2 new pure
consent/ownership guard cases). Existing Windows test assembly and new native tool
compiled; Windows execution NOT RUN. PowerShell preview/execution scripts NOT RUN
(PowerShell unavailable). First compilation identified three nullable checks; fixed
with explicit null rejection/flow annotations, then rebuilt successfully. No warning
suppression, assertion weakening or native fallback. Focused pure tests are included
in the final full portable run; no further run needed after documentation-only edits.

Native CNG generation/export/ACL/signing/store/cleanup checks are all NOT RUN; no
fixture key, certificate, directory or journal was created in cloud. Comparison-key
negative verification uses a disposed in-memory ephemeral ECDSA key (not persisted,
not exported); its purpose is mismatch rejection, never a substitute server identity.
Logs/TRX retained outside repo in `/workspace/stage-a2-2c5-artifacts/`.
