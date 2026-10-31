# A2.2C4 — read-only slice and native proof gate

## Decision and baseline

This is the brief's explicitly permitted **read-only design/tests slice**, NOT a
completed provisioning/storage implementation. The cloud host is Linux; Windows
CNG, DPAPI, native ACL and certificate-store proof cannot execute here. Stop at
that gate. Compilation and public-certificate tests cannot substitute for proof.

Base PR #11 / `76eeb04265b042ca2b0b2137ea15f8ac0e6b1a8c`, stacked on
PR #10. PR #8–#11 remain open when inspected. Reviewed their metadata, existing
snapshot/service/security contracts, C2/C3 reports, AGENTS.md, Program and tray
lifecycle. Owner baseline: Windows SDK 10.0.402 Release restore/build PASS,
654 passed, zero failed/skipped; native tray/physical checks NOT RUN. This is
owner-reported evidence, not a run in this cloud. Prior cloud portable baseline:
613 passed. New branch: `feature/stage-a2-2c4-native-proof-plan`.

## Exact inventory and isolation

- `ReadOnlyFixtureKeyInspection.cs`: internal, explicitly called diagnostic only.
  Accepts only `PBC-A22C4-Fixture-` followed by a nonzero 32-hex GUID. Opens the
  selected key silently in CurrentUser Microsoft Software Key Storage Provider.
  Does not enumerate/select another key or fall back to machine/provider scope.
  Reports only provider/group/size/machine/export-policy metadata. Failures return
  Unavailable, never fabricated missing/protected evidence. No identifiers, paths,
  exception text, key bytes, signing or export operations are returned.
- Six Windows test cases: five invalid selections rejected before native access;
  one random missing-key case opens twice and requires failure both times.
  No fixture creation, mutation or cleanup is performed or needed. Random collision
  with a pre-existing key is astronomically unlikely; if it occurs, the test fails
  without deleting/modifying the key. Never substitute a real user's key name.
- `scripts/Test-A22C4ReadOnly.ps1`: Windows-only explicit confirmation, scope printed,
  locked restore/build and exactly the C3+C4 read-only tests; expected 10 cases.
  Writes build outputs/TRX inside checkout only. Does not start tray or ordinary
  hardware tests. Not a provisioning or mutation harness.
- This report. No existing tracked file changed. No package/dependency change.

MetadataRead is NOT proof: export policy None alone does not prove negative export
behavior, permissions, key possession or certificate association. There is no
conversion to IWindowsSecurityEvidenceSource and no gate activation. No caller in
startup/production. C3 still reports key protection NotInspected and NLM Unknown.
The fixture selector is diagnostic input validation, not an authorization boundary.

## Confirmed direction; no deployment defaults selected

First APK test uses direct home local Wi-Fi; Tailscale later under a separately
verified interface policy. Pinned TLS uses SHA256(DER SPKI), exact SAN/hostname,
validity and reviewed certificate validation plus distinct 256-bit phone bearer.
No trust-all/cleartext fallback or mTLS implementation. Anonymous random authority;
C3's historical .local suggestion is superseded: do not choose .local. Reserved
.invalid app-controlled authority/IP mapping remains a proposal until Android TLS
hostname mapping is proven end to end. No SAN generated here.

Owner direction: suggested explicit configurable 8443, selected private NIC/IP,
no wildcard/fallback; 90-day CurrentUser software-CNG ECDSA P-256 nonexportable leaf,
advance expiry warning; manual rotation/new SPKI requires re-pair. Separate LAN
OFF and per-session approval; persisted desire never restores authorization.
No port/NIC/authority selected, certificate deployed, renewal or consent UI added.

## Future opt-in native fixture harness — DESIGN ONLY

Do not run ad hoc commands to provision identity. A subsequent Windows-capable
stage must implement/review the harness first. It must print exact resource scope,
require an explicit mutation confirmation, and use a fresh random operation GUID
for a test-only key, leaf marker and non-shared local data directory. Before any
write, reject duplicate/pre-existing key/file/certificate selection. Hold exclusive
operation ownership. Persist an ownership manifest; never identify cleanup targets
solely by a friendly name/prefix. No wildcard deletions, PFX, root, LocalMachine,
production names/files or automatic startup registration. A crash leaves an explicit
reviewable orphan; never silently adopt/overwrite it. Cleanup only exact resources
created by the run, in reverse order, with identity/ownership revalidation. Cleanup
failure must report NOT CLEAN, not success. No key/credential contents in logs.

Native proof requires actual software KSP, user scope, ECDSA P-256 algorithm and
export policy observed after reopen; safe negative raw-private/PFX export attempts
must fail, temporary output zeroed and never written; sign a random local challenge
and verify with leaf public key. Verify exact CurrentUser/My association after restart,
no root/system store write, expected SPKI and exact SAN. Reopen selected key without
fallback. Inspect native security descriptor of key and files, owner SID and every
ACE/inheritance; document provider/OS representation and privileged administrator/
SYSTEM boundaries. Do not equate HasPrivateKey or ACL enumeration to inaccessible
under all principals. A separately isolated second-user denial test needs a reviewed
owner-run account procedure; never create accounts/change permissions automatically.

## Protected verifier store — DESIGN ONLY, no DPAPI implementation

Proposed bounded versioned envelope: app/schema/installation/store identity and
generation, max 16 unique anonymous phone IDs, SHA256 verifier bytes only, explicit
revocation/tombstones and rotation metadata; never raw bearer/bootstrap tokens.
Validate size before allocate/unprotect, strict schema/known version/unique IDs,
canonical bounded values and identity binding after CurrentUser DPAPI unprotect.
Use explicit local test directory ownership, no links/reparse redirects, reviewed
DACL on directory/temp/destination, exclusive writer and same-volume temp file.
Write full protected bytes, flush durable data, atomic replace, verify restart read.
Do not treat a rename alone as evidence of power-loss durability on every filesystem.

A Windows fixture must interrupt before write/flush/replace and after replace; reader
sees exactly old/new valid generation, never partial enrollment. Acknowledged revoke/
rotate must survive restart; stale token verifier must never become valid again.
Corrupt, missing, wrong-user/identity, ACL-invalid or unprotect-failed data disables
LAN/enrollment and requires explicit owner recovery; no silent empty approved store,
backup restoration, credential recreation or rollback. Tombstones in a restorable
blob cannot defeat rollback of the entire blob: DPAPI is confidentiality/integrity,
not anti-rollback. Until a reviewed independent monotonic anchor or explicit
recovery/revoke-all policy is proven, adversarial same-user valid-blob rollback
remains a blocker. Copying old authenticated bytes is not a corruption test.

## Required native matrix (ALL NOT RUN here)

| Fixture / fault | Required evidence before go |
| --- | --- |
| Clean create/reopen | Exact user software KSP, P256, leaf association, signing proof |
| Duplicate/pre-existing selection | Refuse before mutation; original resource unchanged |
| Wrong identity / missing key or cert / key loss | Fail closed, no replacement or enrollment reuse |
| Permissions / wrong user | Reviewed ACL plus isolated cross-user denial, no automatic ACL repair |
| Export attempts | Private export refused; no PFX/file/key disclosure |
| Corrupt / oversized / wrong schema or binding | Bounded rejection; no enrollment activated |
| Interrupted update / restart | Old-or-new whole state; no partial acknowledge or silent backup |
| Revoke / rotate / rollback | Durable invalidation; rollback limitation explicitly resolved |
| Dispose / failure / cleanup | Owned exact resources removed or explicit failure/orphan report |
| NLM / NIC changes | Exact NIC ID + assigned/up IP + actual NLM profile; Unknown blocks |

NLM adapter remains design-only: native COM Network List Manager mapping must join
connection adapter GUID to exact selected NIC; all applicable profiles must satisfy
approved policy. Missing/conflicting/transient/access-denied evidence is Unknown.
Reobserve before activation and on network changes; invalidate session approval on
change, never auto-enable on restoration. No profile changes/firewall operations.

## Owner: one step at a time

Step 1, from repository root in ordinary Windows PowerShell, inspect scope without
running tests (expected intentional error, no test execution):

```powershell
.\scripts\Test-A22C4ReadOnly.ps1
```

After reviewing scope, step 2:

```powershell
.\scripts\Test-A22C4ReadOnly.ps1 -ConfirmReadOnlyScope
```

Expected: Release build succeeds; 10 read-only cases pass, zero failed/skipped.
TRX: `test-results/a22c4-readonly/a22c4-readonly.trx`. If counts differ or any fail,
stop and review. No provisioning was tested, even if all ten pass.

Then standard owner regression, separately (may exercise existing native test
fixtures; inspect existing TESTING.md; no physical device request authorized):

```powershell
.\test.ps1 -Release -CopySummary
```

Share only the sanitized summary, not raw store/key inventories or local paths.
Stop for review after results. Do not proceed to mutation fixtures, TLS, enrollment,
Android connectivity or physical tests. GO requires reviewed Windows mutation harness,
all matrix results, cleanup evidence, resolved rollback/ACL limitations, validated
hostname mapping and explicit owner approval. This stage is NO-GO for deployment.

## Cloud verification

Results recorded after commands complete in the stage report. Native Windows tests
and the PowerShell script cannot run on Linux and must be marked NOT RUN. No native
proof, tray, hardware, TLS handshake, LAN, pairing or physical phone validation.
Snapshot-v1 files, battery/gateway/HID scheduling/consent, tray startup/shutdown,
Android and recovery exports remain untouched. No listener, firewall/router change,
merge, rebase or release.

Final executed cloud results: locked solution restore PASS; Release solution build
PASS, 0 warnings/errors; portable suite 613 passed, 0 failed, 0 skipped. Six new
Windows test cases compiled, NOT RUN; four prior read-only Windows cases also NOT
RUN. Windows PowerShell script execution NOT RUN (PowerShell unavailable on host).
Initial sandbox build/test attempts did not complete; test runner IPC socket was
permission-denied. Authorized elevated rerun completed successfully, without proxy
changes. Native security matrix remains entirely NOT RUN. No claim of CNG/DPAPI,
ACL, persistence, cross-user, TLS or physical-device proof.
