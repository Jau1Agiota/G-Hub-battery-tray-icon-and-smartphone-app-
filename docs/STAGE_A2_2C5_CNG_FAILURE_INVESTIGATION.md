# A2.2C5 — native export failure investigation; NO mutating retest

## Evidence and exact failure boundary

Base PR #13, `1f09098a00c1b66237589cc801643bb51d4406a2`.
Owner approved one execution, operation `3c4a7c190ef34f2e90625276aa9cbfdb`.
Owner restore/build and six distinct prerequisite proof cases passed, including actual
reopened software-P256/user/export-policy metadata. Repeated ancestor checks print
multiple PASS lines but count as one named case. Then CryptographicException
`0x80090029`; harness reported cleanup; 12 remaining proof cases NOT RUN, overall FAIL.
This is owner evidence, not native execution by Codex.

Static control-flow inspection pinpoints the managed call:

```csharp
reopened.Export(CngKeyBlobFormat.EccPrivateBlob)
```

After the last reported PASS, the only potentially throwing native/managed crypto
operation before the next assertion is that export. The old catch filter accepted
only 0x8009000B/0x80090010; 0x80090029 escaped to the outer catch. Therefore the call
boundary is identified from code and the reported trace. We do not possess a native
stack/trace and cannot identify whether NCrypt's sizing or buffer-fill call failed,
or establish whether policy caused this particular provider to return unsupported.
No unsupported HRESULT is evidence that exportability has been safely proved.

## Official API semantics / defensible interpretation

Microsoft references consulted:

- [CngKey.Export](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.cngkey.export?view=net-10.0):
  formats depend on underlying KSP support; CryptographicException covers all other
  errors, *typically* export policy. "Typically" is not a cause guarantee.
- [NCryptExportKey](https://learn.microsoft.com/en-us/windows/win32/api/ncrypt/nf-ncrypt-ncryptexportkey):
  BCRYPT_ECCPRIVATE_BLOB requests an ECC private blob. Format/provider/operation support
  matters; it is not interchangeable with exporting public SPKI or a PFX.
- [CNG property identifiers](https://learn.microsoft.com/en-us/windows/win32/seccng/key-storage-property-identifiers):
  NCRYPT_EXPORT_POLICY_PROPERTY is metadata; allow-export and allow-plaintext-export
  are different flags. Observed None is useful, but cannot establish every operation's
  behavior or attribute an error to a particular policy.
- [Microsoft HRESULT definitions](https://learn.microsoft.com/en-us/windows/win32/com/com-error-codes-4):
  NTE_NOT_SUPPORTED 0x80090029 = requested operation not supported;
  NTE_BAD_KEY_STATE 0x8009000B = key not valid in specified state;
  NTE_PERM 0x80090010 = access denied.

Outcome decision table (implemented as pure logic):

| Observation | Meaning | Continue this rejection-observation check? |
| --- | --- | --- |
| Export returned bytes | Export succeeded; zero buffer; fail | NO |
| 0x80090029 | Unsupported/inconclusive, not denial proof | NO |
| 0x8009000B | State rejection observed, policy causality unproved | YES, observation only |
| 0x80090010 | Access denial observed, policy causality unproved | YES, observation only |
| Any other error / missing result | Unexpected/unavailable evidence | NO |

No new HRESULT is accepted. Existing accepted outcomes remain only observed state/
access rejection, now explicitly labeled **not policy-causality proof**. There is no
fallback blob, relaxed export policy, automatic retry, all-CryptographicException PASS,
or change designed to turn the owner's 0x80090029 into green. It now gets a precise
failed check instead of looking like a check never reached. Metadata, raw export
observation, ACL evidence and effective nonexportability remain separate assertions.
Even a fixture RESULT PASS does not validate production nonexportability or ACLs.

## Future verification strategy — proposal ONLY, not implemented or authorized

Before changing the proof strategy, review a provider-appropriate test design with
actual Windows evidence. Keep the persisted fixture explicitly nonexportable and
reopen its exact provider/group/curve/scope/ExportPolicy. Use a documented native
export operation with explicit buffer/format handling; clear every private output on
all paths, never PFX/disk. Do not try multiple formats until one yields a desired code.

A separately approved positive-control experiment could use one ephemeral, never
persisted/exported-to-disk software-KSP P256 comparison key with explicitly exportable
policy, the exact same export API/blob and process/provider settings. Successful
in-memory comparison export would establish that configuration supports that blob;
zero output immediately. A rejected comparison makes the experiment inconclusive.
A differing persisted-key outcome still needs review of key state/ACL/provider
semantics; unsupported on the target does NOT automatically become policy denial
merely because comparison succeeded. This probe is **not implemented**, not included
in the current scope and must not execute without fresh owner authorization. No
cross-user/effective-ACL claims or account changes follow from it.

## Code corrections / cleanup audit

New pure FixtureDiagnostics class provides closed operation IDs, bounded category/
HRESULT-only formatting, export interpretation and explicit cleanup states. Native
API checks print CHECK operation IDs before access; exceptions identify the active
operation. Raw exception messages/types outside an allow-list, paths, SIDs, key bytes,
private blobs and certificate inventory are never emitted by diagnostics.

At the export boundary all CryptographicExceptions are classified, not accepted:
unsupported/unknown become a failed assertion; private bytes, if unexpectedly returned,
are zeroed in finally before assertion failure. Other native operations retain their
checks. No native export API, key/store scope, resource selection or cleanup strategy
is changed. No exportable comparison identity was added.

Cleanup audit of the owner's old trace: code requires live exact journal bytes, exact
operation-derived key name and matching public SPKI; deletes only that key after any
exact DER+thumbprint owned leaf removal; checks CngKey.Exists false; records journal.
The trace thus supports **harness-reported key cleanup**, not independent absence
proof. Failure occurred before leaf creation, so claiming a leaf was cleaned was
inaccurate. The final combined "confirmed/no-key-created" string was also wrong.
The journal is retained; Codex has not opened, modified or independently checked the
owner's machine or evidence. No fresh cleanup or orphan scan is authorized.

New states distinguish no creation attempted, creation outcome uncertain, created
cleanup incomplete, and created then cleaned. Key-only cleanup explicitly says leaf
not created. Successful removal says "by-harness" / "not-independently-verified".
If failure happens after deletion but before successful journal recording, cleanup
stays unconfirmed. The finally block still uses exact identity and original ownership
requirements, never prefix selection, orphan adoption or retry. Fixture files may
remain even if no key creation was attempted; status states that explicitly.

Failed active assertions are recorded as FAIL rather than NOT RUN; unreached cases
stay NOT RUN. Outer infrastructure errors have an explicit operation-errors count,
so an overall FAIL with zero failed named assertions cannot look successful. Named
case counting still deduplicates repeated ancestor checks. Cleanup is independent
of proof-case totals and overall result still requires both proof completion and
successful cleanup. Uncertain cleanup is always non-successful.

The retired operation is refused case-insensitively in both pure CLI validation and
PowerShell wrapper before restore/build; it cannot be reused for creation or cleanup.
Do not edit its journal, delete anything or rerun it. No automatic recovery command.

Changed files: harness Program.cs, new FixtureDiagnostics.cs, FixtureConsent.cs,
Invoke-A22C5Fixture.ps1, portable project source link, new pure diagnostics tests,
this report and top-of-original-report stop notice. Existing tests/assertions remain.
No tray, gateway/HID/polling, snapshot-v1, Android, protected registry or unrelated
untracked files touched. No reset/clean/merge/rebase/release.

## Verification and owner gate

Cloud: Linux/.NET SDK 10.0.403. Locked restore PASS; Release solution build PASS,
0 warnings/errors. Portable tests **626 passed, 0 failed, 0 skipped**: previous
615 plus 11 pure cases covering unsupported/unknown/success/error classification,
redacted diagnostics, exact cleanup wording and retired-operation rejection.
No keys/certificates/stores accessed or mutated by these new tests.
Windows-native test assembly/harness compiled only. Native CNG/export/ACL/signing/
association/cleanup execution NOT RUN. PowerShell execution NOT RUN. No fixture
creation/deletion or journal access, DPAPI, TLS, listener, pairing or physical test.
Logs/TRX retained in `/workspace/stage-a2-2c5-investigation-artifacts/`.

STOP after code review. **Do not rerun the failed operation.** No native command is
requested by this delivery. Review the error interpretation, diagnostics and cleanup
correction first. Any later native experiment needs a NEW preview-generated v4 ID,
explicit approval of its exact scope/strategy and ordinary unelevated PowerShell.
Do not infer native approval from portable test success. DPAPI, transport, Android
and production security activation remain blocked and outside this task.
