# Owner packet: source-only harness admission review — NOT APPROVED

Baseline native driver: `0405d77331a378ea63c5cea5f5fcdc8b1b6ac4a3`, draft PR #33.
This packet contains no credential or approval token. Do not commit actual user
profile paths, passwords, VM exports or private evidence to the public repository.
The owner retains the completed packet privately and records its public digest.

## Required bindings (blank means incomplete)

| Field | Owner-supplied value/evidence |
| --- | --- |
| VM and recovery | Hypervisor/version; VM ID; clean snapshot ID; golden-image SHA-256; recovery custodian |
| Guest | Exact Windows edition/build/update level, x64, vCPU/RAM/disk allocation; fixed NTFS and free-space observation |
| Isolation | Host-side disconnected vNIC evidence; no sharing/clipboard/redirection/USB/HID; staging medium detached; no real data/domain |
| Account | Existing disposable local standard account SID, actual LocalAppData; independent membership attestation; no password in packet |
| Private paths | Artifact root and nested directories; existing acceptance parent; read-only owner/DACL/reparse observations; separate approval for any missing provisioning/ACL preparation |
| Source provenance | Full driver commit, full preflight commit/hash, clean relevant source/index scopes, approved builder and build log/lock hashes |
| Binary provenance | Exactly eleven artifact names/SHA-256/byte sizes from the builder and matching staged copy; OS/runtime trust and exact versions |
| Manifest | Exact UTF-8 bytes, SHA-256, schema 1, run/identity UUID, apphost path, SID, LocalAppData, eleven hashes, source commit, whole-UTC windows |
| Time | Stable clock/time-source evidence; <=1-hour execution window inside <=24-hour certificate lifetime; fresh packet if stale |
| Read-only preflight | Reviewed script hash/version, invocation parameters, complete stdout JSON, collection time; FAIL/MISSING/UNKNOWN/NOT_OBSERVED gaps kept distinct |
| Retention | Owner accepts intentional journal/key retention, checkpoint preservation and no automatic uncertain-resource cleanup |

## Decision A: authorize an admission REVISION, not execution

Status: **NOT REQUESTED / NOT APPROVED**. Suggested exact scope for later owner
decision after the bindings are reviewed:

> Authorize source-only implementation and review of a harness-only admission
> revision based on the identified driver commit and this privately frozen resource
> packet. Limit admission to the named disposable VM/account, exact source/build
> artifacts, fresh run resources, closed case table and approved UTC window. Preserve
> every production Gate E/W1 hold, native test skip, Gate B boundary and networking
> prohibition. Permit static/managed/denial verification only. Do not execute native
> cases, create keys/credentials, operate DPAPI/CNG, modify ACLs or terminate processes.
> Return a draft diff, genuine managed results and newly bound artifact/manifest
> hashes for independent review. This is not native execution authorization.

The revision may not introduce a general enable flag, arbitrary executable/case/
path input, environment switch, reflection escape or reusable bearer permit. The
current unconditional harness entry/scope holds stay in place until that source
change is independently authorized/reviewed; **production holds never change under
Decision A**. Do not make this preparation's preflight an admission issuer.

All rebuilt binaries and the final manifest must be rebound after the admission
revision; current 0405d77 build hashes cannot approve changed binaries. Source
review, manifest approval and later execution are separate records, avoiding a
binary/manifest self-hash cycle. A completed resource manifest alone grants nothing.

## Decision B: any future native batch remains a separate decision

Status: **NOT REQUESTED / NOT APPROVED**. Only after Decision A's exact source,
binary hashes, final manifest and VM evidence receive review may the owner consider
an independent native authorization. Its side-effect scope must explicitly name:

- The existing [23-slot plan](gate-e-native-execution-manifest.md), fresh process
  isolation, 30-second case deadline, five-second exit confirmation, one-process
  Job and exact owned-process termination for the six phase stops.
- ONE exact CurrentUser fixture key and certificate, 106 DPAPI reservations
  (128 ceiling), <=8 signatures / <=16 verifications, eight nonce samples, one
  ephemeral bootstrap and one fixture protected credential; no production use.
- The exact private NTFS run root, eleven bounded journal scopes, path/ACL/link
  fixtures and evidence paths. ACL mutation, native keys, credential generation
  and process termination are forbidden NOW and require that future explicit scope.
- Intentional uncertainty retention and no automatic cleanup; a subsequent
  exact-resource cleanup decision is separate. Unknown exit never means clean.
- Second-account/foreign-owner cases remain NOT RUN without an added reviewed
  scope. No real TLS, network, Stage E4, hardware, trust-store changes, universal
  private-export or power-loss proof. This batch cannot automatically accept Gate E.

There is no native run command in the preparation/preflight procedure. Do not run
the harness even to see its blocked exit code. Do not merge PR #33 or interpret
either review packet as production security acceptance.

## Current gap record

No disposable VM/snapshot, actual SID/LocalAppData, prepared private paths, approved
image/runtime, staged binary hashes, completed manifest fingerprint or live VM
preflight has been provided. Missing folders/account/ACLs need separate custodian
preparation authorization; this task supplies none. Decision A and Decision B are
both outstanding. Production Gate E remains **BLOCKED and UNAUTHORIZED**.
