# S0 → future W1 approval gate — specification only

[Canonical static review and source references](S0_STATIC_WINDOWS_API_POLICY_REVIEW.txt)

**No box is an execution permit. Nothing below ran in S0.** No W1 implementation,
test project, operation ID, key or certificate exists as a result of this review.

## Scope decision before drafting an executable approval request

- [ ] Owner explicitly accepts or rejects a **revised public-leaf-only W1**. It
  tests an unnamed nonexportable P-256 key and a separately owned public test leaf;
  it does not establish a private-associated TLS identity.
- [ ] Owner accepts or rejects **test-only ACL evidence NOT ESTABLISHED**. The
  documented security-descriptor property applies to persistent keys. Current-user
  scope is not owner-DACL proof. Never set production ACL evidence to true.
- [ ] Keep `ExportPolicy=None`; no exportable PFX/DPAPI replacement or weakening
  of `LanApprovalSession.NonExportable/CurrentUserAclRestricted` requirements.
- [ ] Explicitly exclude `ECDsa.Create(curve)` factory defaults and
  `CreateSelfSigned`/`CopyWithPrivateKey` implicit private-export/import path found
  in v10.0.0. No internal helper/reflection/PInvoke workaround.
- [ ] If the original associated-certificate objective remains required, stop and
  commission a separate association design. Do not run the public-leaf experiment
  and describe it as that stronger proof.

## Exact platform / policy sheet — all values require owner review

- [ ] Windows edition/build/architecture, normal user/profile context, no elevation
  or impersonation; exact SDK **and runtime servicing version** and mapped source.
  `global.json` alone does not select the runtime or establish OS behavior.
- [ ] Re-audit the selected runtime's CngKey creation, ECDsaCng duplication,
  signature generator, CertificateRequest.Create and certificate loading/disposal
  paths. S0 source inference is pinned to **v10.0.0**, not every future patch.
- [ ] Explicit Microsoft Software KSP; algorithm ECDsaP256; keyName **null**;
  creation options None; export policy None; Signing usage; no parent window;
  UIPolicy null; no custom properties. No provider/name/flag fallback or retry.
- [ ] Exact single canonical private IPv4 **or** ULA test SAN; no listener/interface/
  port required. Exact UTC validity/clock confidence, public serial and synthetic
  subject, extension criticality, public observation/output bounds.
- [ ] Confirm public-only parameters/SPKI/DER reads, hashing, signing and native
  certificate loading are explicitly allowed in that future approval. No private
  export probe, PKCS12, store, DPAPI, Keystore, certificate trust installation,
  chain/network validation, old fixture access or separate comparison key.
- [ ] Resource budget: one private key, original CngKey plus one signer duplicate,
  one public leaf, reviewed transient public objects/buffers. Unknown hidden key
  generation/import or extra object allocation is a blocker, not an allowance.
- [ ] Permitted observations and release evidence fixed. If managed Dispose is not
  enough to establish required cleanup, label unconfirmed and STOP; no automatic
  native instrumentation or deletion.

## Runner / discovery review — no discovery now

- [ ] A separate future `PeripheralBatteryCompanion.W1Offline.Tests` project is
  reviewed and approved before creation; it currently **does not exist**.
- [ ] No solution membership/default-runner/production project references, no linked
  legacy test/fixture source, no module/assembly/class/collection crypto fixtures,
  no theory-data generation, native constructors or setup/teardown key creation.
- [ ] Inspect resolved SDK/NuGet imports, generated code and adapter/discovery
  callbacks. Unknown is unsafe. A filter cannot undo discovery side effects.
- [ ] Only the exact proposed native method is allowlisted:
  `PeripheralBatteryCompanion.W1Offline.Tests.W1OfflineLifecycleTests.SingleShot_UnnamedNonExportableP256_PublicLeaf_NoStore`.
- [ ] Proposed pure preflight names are separately reviewed, not automatically run:
  `W1OfflineGateTests.MissingOwnerConsent_StopsBeforeProviderOpen` and
  `W1OfflineGateTests.InvalidCanonicalIp_StopsBeforeProviderOpen` in the same proposed
  namespace. No existing tests are renamed, skipped or weakened to fit this plan.
- [ ] Independent reviewer confirms one matching case/binary/source version,
  no parallelization/retries, owner gate before first provider open and process-local
  one-use invocation. New process is not automatic permission for another attempt.
- [ ] Review the **documented-only** command in the canonical report. No unfiltered
  `dotnet test`, `test.ps1`, negative-only filter, wildcard, old harness, preview or
  solution build. `--no-build --no-restore` still launches a test host and discovery.
- [ ] Build/restore/discovery permission is separate from native execution. Approve
  exact command/bits/results path and all side effects before use; no command ran here.

## Future single shot — only under a new explicit approval

1. Verify platform, reviewed source/binary, consent, canonical IP, time and bounds
   before mutation. On ambiguity, do nothing and stop.
2. Create one null-name None-policy key; inspect requested-versus-observed approved
   metadata. Unsupported or mismatching metadata, provider UI or error stops.
3. Wrap that existing key; sign one **public** leaf via the explicitly reviewed
   generator/Create overload. Expected `HasPrivateKey=false`.
4. Check public SPKI, exact type7 IP SAN, serverAuth, digitalSignature, non-CA and
   clock bounds. Wrong expected pin/IP comparisons reuse this same public leaf.
   Missing-EKU/expired/other native fixtures remain not run unless separately scoped.
5. Dispose owned public leaf, signer duplicate, original key in that order. Record
   managed versus native release certainty; exceptions are not proof of cleanup.
6. Stop for review. No second attempt, deliberate crash, export/import, named-key
   deletion, broad enumeration or recovery. No production activation.

## Result / stop conditions

- [ ] Report request flags, actual metadata and evidence limits separately. A None
  readback is not actual private-export denial or causal policy proof.
- [ ] Chain trust, native TLS, Android anchoring, ACL protection and production
  nonexportability remain unproven. No signature/field match upgrades these claims.
- [ ] Unexpected UI, named resource/store change, private-export path, extra key,
  protection/disposal uncertainty or ambiguous test selection → **STOP**, preserve
  sanitized evidence, owner review. No speculative cleanup or automatic retry.
- [ ] Redact secrets/user paths/handles; no raw private key or real credentials.
  Existing retired/preview IDs and all old journals/keys remain untouched.

## S0 source-control gate

- [ ] Documentation only; prior records/source/tests/CI/configuration unchanged.
- [ ] `[skip ci]` in docs commits; no job dispatch/rerun or runtime checks.
- [ ] Dedicated S0 branches and draft PRs target D3.3B, remote SHAs verified.
- [ ] Native **NOT RUN**; LAN **NOT RUN**; production security **NOT PROVEN**.

**Recommendation: REVISE before an executable W1 request. The single next owner
decision is whether to accept the revised public-leaf-only objective with ACL
evidence explicitly unestablished. No implementation or run is approved.**
