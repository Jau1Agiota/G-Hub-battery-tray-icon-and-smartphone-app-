# Isolated native driver execution manifest — NOT AUTHORIZED / NOT EXECUTED

Driver: `tools/GateE.NativeAcceptance/`. Baseline source `a5a40ac`; the final
implementation commit and its built artifact hashes must be recorded in the
completed manifest before any native review. No native case has run. The executable
entry point AND isolated case admission unconditionally throw. Filling a JSON file
cannot authorize execution. Production Windows holds remain unconditional too.

This is the exact **closed case/resource plan** implemented by the driver. The
adjacent [JSON template](../tools/GateE.NativeAcceptance/execution-manifest.template.json)
is deliberately invalid until its VM/account/path/time/provenance fields are bound.
It has no approval boolean, secret, reusable permit, environment switch, case name
extension or arbitrary executable option. The private child stdin barrier is
process coordination only; it is not native authorization.

## Decision still required

The owner must select a disposable nonadministrator VM account, its exact SID and
LocalAppData, an owner-only private artifact directory and existing private
`<LocalAppData>/PBC.GateE.Acceptance` parent, a <=1-hour run window and <=24-hour
whole-UTC certificate validity. Freeze the exact source revision, eleven artifact
SHA-256 hashes, runtime/OS, and completed public manifest. The reviewed executable
must be an apphost, not `dotnet run` or a testhost.

Then separately authorize review/implementation of the **harness-only admission
release for those frozen resources/cases**, and execution of that batch. Current
source has no runtime release mechanism and cannot run protected cases. A later
admission revision requires source review and renewed build/hash binding; it must
not remove production holds or permit arbitrary manifests. This document does not
ask for that approval now and does not authorize it. Production Gate E acceptance
and any networking remain separate decisions.

## Exact owned resources and budgets

| Resource | Fixed scope / bound |
| --- | --- |
| Run UUID | Template `9b617169-d7c9-46bb-9a90-6695bbc26673`; never reuse after an attempt. A new reviewed UUID is needed after partial execution. |
| Identity UUID/key | `bbefb702-e234-42c5-af73-7398515cedac`; exact `PBC.Identity.bbefb702-e234-42c5-af73-7398515cedac`, CurrentUser Microsoft Software KSP, one P-256 signing key, no overwrite/private export/enumeration. Existing exact name stops the batch. |
| Root | Derived ONLY as `<exact LocalAppData>/PBC.GateE.Acceptance/<RunId>`, <=180 characters; must not exist. Exact live account SID and LocalAppData must match. Fixed NTFS only. |
| Artifacts | Exactly the ELEVEN executable/config/managed/native dependency files in the JSON template, each <=64 MiB. Includes Core, HyperXHelperProtocol, Microsoft.Data.Sqlite, SQLitePCLRaw and win-x64 e_sqlite3; no database is opened. Manifest must be `execution-manifest.json` adjacent to the executable. Extra executable files/directories are rejected; only three known PDBs may be additional files. Prepare a selective private artifact copy, not the unrestricted multi-platform build-output directory. File handles deny write/delete sharing; directory metadata pins deny delete sharing while allowing independently guarded writable journal handles. |
| Evidence | ONE sibling `<root>.evidence.json`, created new under the pinned private parent before launching cases, <=1 MiB typed JSON. Must not exist. Bounded `filesystem.results.json` inside root records ACL/link limitations. Never raw exceptions, buffers, tokens or database content. |
| Public fixture data | Root `public-leaf.der` <=4 KiB and `identity.receipt` <=128 bytes. One self-signed public certificate; never imported into a Windows certificate store. SAN `192.168.100.10` as data only, never bound/connected. |
| Credential | ONE `secrets/fixture-credential.bin` DPAPI ciphertext <=16 KiB; raw bootstrap/credential remains memory-only and is zeroed. Nothing enrolled/registered/used. |
| Journals | `clean`, six `crash-<phase>` directories, and `faults/write`, `faults/replace`, `faults/flush`, `faults/close`: 11 journals. Each only the production nine names and <=64 KiB; envelope/replay/record bounds unchanged. |
| Path fixtures | `filesystem/acl-broad`, `acl-null`, `acl-inherited`, `target`; transient `alias.bin`, `target.bin`, `link`, wholly inside root. Broad/null/inherited ACLs are restored to verified owner-only after expected refusal. No foreign owner or privilege escalation. |
| Process isolation | 23 closed plan slots, at most 24 children; sequential fresh processes, 30-second operation deadline plus 5-second confirmed-exit wait. Metadata-bound retained handles, actual parent/PID/start/exit, no test DLL in monitor. |
| Containment | An unnamed Job with ActiveProcessLimit=1 and kill-on-close, assigned to the verified child before releasing the private stdin barrier. No breakaway, descendant allowance, process-name kill or shell. Unknown assignment/exit stops and retains; Job close is not claimed exit proof. |
| Crypto | Static sum reservations 106 DPAPI admissions (hard aggregate ceiling 128), <=8 fixture-key signatures / <=16 public verifications. Invalid-input admissions also consume budget. One key creation, <=8 nonces, one bootstrap, one credential; no refunds/retries. |
| File/cleanup limits | Additional per-case 20,000 guarded file admissions; cleanup inventory <=24 directories / 512 nodes, exact closed names, no recursive delete or reparse traversal. |

All resources are test-only. No current repository file, real application data,
existing key, account provisioning, hardware, network endpoint, certificate store
or production startup is in scope. Public certificate parsing/imports and exact
key metadata reads occur in the listed bounded cases; they are not private-key
creation or extra signature operations. Batch runtime cannot establish physical
power-loss behavior or universal private-export denial.

## Fixed process order

| Slots | Case IDs | DPAPI reservation | Expected behavior, not an observed result |
| --- | --- | --- | --- |
| 1 | prepare | 0 | Fresh private run root; SID/nonadministrator/LocalAppData checks before protected primitives. |
| 2 | identity | 0 | Create/close/reopen exact key; issue one certificate; strict profile/self-signature, leaf/pin/IP/time and signed challenge rejection checks; receipt after verification. |
| 3 | protection | 12 | Synthetic record/envelope transforms, corruption and wrong endpoint/identity/leaf binding; zero/oversize bounds. |
| 4 | entropy | 2 | Eight nonce samples, one ephemeral bootstrap, one credential delivered to fixture DPAPI store before verifier. Samples do not prove entropy quality; no additional native failed-sink generation is budgeted. |
| 5 | filesystem | 0 | Actual broad/null/inherited ACL, hardlink and optional symlink refusals; foreign-owner fixture NOT RUN, no elevation. An unavailable symlink is NOT RUN. |
| 6–9 | clean-write, clean-read, retire, retired-read | 4, 2, 4, 2 | Durable record and replay CAS, clean restart high-water version, tombstone restart; no exposure. |
| 10–21 | crash-witness/read-witness, crash-pending/read-pending, crash-state/read-state, crash-checkpoint/read-checkpoint, crash-intent-removed/read-intent-removed, crash-session-removed/read-session-removed | 6 + 2 per pair | Retained child is terminated only AFTER its exact flushed phase notification. Except witness, hooks arm after fresh initialization so authorization transaction interruption retains session evidence. Session removal may reopen clean; other abandoned phases deny. |
| 22 | faults | 32 | One bounded write/replace/flush/close injection per fresh owned journal. Injection must actually fire; an unrelated native failure cannot count as the expected rejection. Cleanup uncertainty is never an expected-denial pass. |
| 23 | cleanup | 0 | Conditional only. Attest all paths/handles/names before deleting the exact receipt-bound key and nonrecursive owned files. Failure/NOT RUN/uncertain evidence or intentional fault case suppresses this dispatch. |

**The full default plan deliberately retains resources and reports cleanup NOT RUN.**
Foreign-owner coverage has no authorized account/privilege, and injected journal
uncertainty is preserved for review. The owner cannot infer cleanup success or full
Gate E acceptance from successful rejection cases. The cleanup implementation is
source-complete but is not automatically dispatched by this full-failure plan; any
subsequent exact-resource cleanup requires its own owner-reviewed admission/plan.
Second-account DPAPI is explicitly NOT RUN because schema 1 has no account launcher.
No account credential is manufactured. Host power loss and real TLS are outside
this batch. These limitations are recorded rather than reported as passing cases.

## Evidence and failure accounting

The process model records only typed known case IDs, enum outcomes, integer IDs,
UTC ticks, exit codes, termination request and confirmed-exit flags. JSON outcome
codes are 0=Passed, 1=Failed, 2=NotRun, 3=Uncertain. ErrorKind is restricted to
None/Unavailable/HResult/Win32; child diagnostic lines contain only that kind and
one canonical integer. DPAPI preserves its captured Win32 failure; errors whose
original native code is unavailable remain labelled managed HRESULT/unavailable.
Exception text/stack and stderr are never copied into evidence.

Native runtime metadata includes OS/runtime/architecture and fixed provider;
source/artifact binding is the adjacent immutable manifest. Case output <=128
characters; stderr drain <=1 KiB. A mismatch, timeout, unexpected exit, unconfirmed
Job assignment, malformed output or unprovable ownership fences the remaining plan.
Only the retained exact child may receive termination; unknown exit retains all
resources, even if kill/Dispose returned. Retention is not cleanup success.

Read-only fixture/monitor directory pins are harness-only; the production
constructor defaults remain unchanged. They retain ancestry without claiming that
a read-only directory handle can flush durable journal state. Mandatory directory
flush remains in the actual writable production journal path. Public fixture and
evidence file Flush(true) is not a power-loss/directory-durability claim.
The account owner must establish a standard VM account; the runtime administrator
check observes token role and does not independently prove group membership under UAC.

The driver does not subscribe to complete OS process-event tracing. Retained
handles attest the observed child; Job containment, parent inspection, timeouts
and kernel close/kill behavior still need native proof. No process trace or cleanup
claim in this source milestone has been validated by managed fakes.

Proposed command shape, **NOT EXECUTED and currently BLOCKED**:

```powershell
& '<reviewed-private-artifact-directory>\GateE.NativeAcceptance.exe' --manifest '<same-directory>\execution-manifest.json'
```

No build, test or CI command invokes this native command. `test.ps1` builds the
driver and runs only managed policy/denial tests; it never dispatches native cases.
