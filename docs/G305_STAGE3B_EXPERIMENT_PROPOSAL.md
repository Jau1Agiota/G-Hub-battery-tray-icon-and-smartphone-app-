# G305 Stage 3B — proposed experiment, NOT authorized or executable

Status: **NO-GO**. No complete packet can be justified from current evidence. This document is for review, not a hardware-test command. Stage 3A adds no sender, report parser, request bytes, native imports or read loop. HyperX authorization and its packet/timeout experience do not authorize a G305 experiment.

## Proposed sequence and unresolved fields

A future experiment must choose **one purpose and at most one request**, not a discovery-plus-battery sequence. Resolving version, route, feature index and battery semantics may require separately reviewed stages; do not bundle them to satisfy a “one-shot battery” label. Current evidence cannot support a direct battery request. A documented identity/version experiment may precede battery research only after its own gates are met.

| Potential wire field | Purpose / documented general rule | This receiver's proposed value |
|---|---|---|
| Report ID / collection | 0x10 short 7 bytes, 0x11 long 20 bytes in manufacturer overview | Owner verified COL01 input/output button ID 0x10 (7 bytes), COL02 ID 0x11 (20 bytes); exact active choice and routing still **UNRESOLVED** |
| Device index | Address routed wireless target; 0xFF documented for receiver/corded device | **UNRESOLVED**; never default to 0xFF or slot 1 as a G305 mouse |
| Protocol family | Select legacy register vs feature semantics | **UNRESOLVED**; no generation inferred from size |
| Feature index (2.0 byte 2) | Runtime index; root is documented index 0 | Battery index **UNRESOLVED**; 0x1000 is feature ID, not index; root 0 is not battery |
| Function/software (2.0 byte 3) | Function high nibble, software ID low nibble in conventional byte layout | **UNRESOLVED** function/ID; root function 0 GetFeature and 1 GetProtocolVersion are documented, not selected for execution |
| Register/operation (1.0 alternative) | Legacy register addressing | **UNRESOLVED**; do not overlay an assumed legacy register on 2.0 fields |
| Parameters / reserved bytes | Function-specific arguments and exact padding | **UNRESOLVED**; no filled-in byte array; battery return format/function unverified |
| Expected response/errors | Exact ID, length, route, family-specific function/register, software echo and documented status | **UNRESOLVED**; unsupported/legacy error layouts must be established before decoding |

No arbitrary software ID, “likely” wireless slot, extra initialization command or fabricated zero-padded packet is selected. Complete report bytes, exact chosen collection/directions, write API/transport and expected reply/error layout must be presented before implementation approval. HID++ root GetFeature is a **protocol command**, distinct from Windows HidD_GetFeature; neither is allowed in Stage 3A.

## Proposed safety envelope for a separately authorized tool

- Windows-only, separate explicitly invoked console tool; never tray/background startup/provider. No privilege escalation, configuration/pairing/firmware/LED/DPI writes, G HUB termination, driver/audio changes or other discovery commands.
- Exact VID 046D/PID C53F/MI_02 and one separately approved COL allowlist, plus validated usage/descriptor IDs/directions/lengths. COL01 and COL02 are candidates, not an approved active allowlist. COL03 and G915 046D:C547 remain excluded. Reject duplicates, ambiguous physical parents, missing metadata and re-enumeration identity changes before submission.
- Minimum documented access for the selected future transfer, FILE_SHARE_READ|FILE_SHARE_WRITE, OPEN_EXISTING; no exclusive fallback. Access denied/sharing violation stops with numeric Windows error. A zero-access passive open proves neither future read/write access nor coexistence.
- One process/one outstanding exchange; at most one output report; no retries, polling, slot scan, drain, initialization or automatic follow-up. If matching requires unbounded input filtering, stop. Proposed future budget: one bounded input completion only; unrelated/short/extra/ambiguous input ends inconclusively rather than reading again. No input sampling now.
- Proposed **three-second monotonic total exchange deadline**, including submission/completion; never renew it after output or unrelated input. Outer helper startup/termination also needs bounded supervision. Timeout does not prove the device received no request; never resend automatically.
- Use an isolated supervised helper if native I/O can outlive cancellation. Cancellation requests are not completion: cancel pending I/O, observe completion before freeing OVERLAPPED/buffers/handles, or retain ownership within a contained helper until verified process exit. Parent timeout, killed child, unplug, cancellation and launch/IPC failures must have explicit cleanup tests. Unknown child exit means latch unsafe/refuse another attempt, never claim successful cleanup. Do not reuse HyperX wire/native code without a fresh applicability review.
- Bound reports/logs to approved size/count. Log stage/result, numeric errors, sanitized receiver/collection labels, deadline outcome and cleanup status. No machine instance IDs, serials, account paths, arbitrary input dumps or G HUB data. Malformed output is failure, not a reading.

## Response acceptance and safe outcomes

Before any future send, validate exact target and clear experiment purpose. After a single bounded input completion, require exact descriptor-approved report ID/length, route and documented family-specific reply/error fields, software echo where applicable and any documented function echo. Validate battery ranges/enums only after semantics are verified. Reject mismatches/unsolicited events/unsupported-version errors, never reinterpret another device's reply. Same-looking traffic, missing ownership evidence or concurrent G HUB requests stays **Candidate / Ambiguous** at best, never confirmed fresh. No report becomes a production battery reading.

Return distinct sanitized outcomes: target missing, multiple/identity-unresolved, shared-open denied, unsupported metadata/protocol, unexpected/ambiguous response, timeout, cancelled, disconnected, native/IPC failure, cleanup-unconfirmed. Unknown/asleep/offline are not distinguished from silence alone. No automatic fallback, disconnect recovery query or process restart query.

## Minimum next non-transmitting evidence

Stage 3A.1's cached-capability extension is implemented and the owner has returned its successful passive result (see matrix below). Do not repeat it simply to reconfirm IDs. Stage 3A.2 recommends accessible public manufacturer routing/error specifications for offline review; no additional live report capture or hardware command. See [the identity/protocol review](G305_STAGE3A2_IDENTITY_PROTOCOL_REVIEW.md) for precise gaps and the receiver-versus-mouse distinction.

## Go / no-go and approval checkpoint

Stage 3B remains NO-GO unless reviewers can answer all applicable gates for its **single chosen purpose**:

1. Applicable primary protocol sections and descriptor evidence justify every field/size/transport and error decoder; version/route/identity established or the narrowly scoped identity experiment explicitly treats them as its unresolved result. A battery experiment additionally requires verified battery feature/register/function semantics and runtime index.
2. The target cannot silently become G915, another C53F receiver or receiver-only presence. Mouse identity/liveness limits and G HUB correlation limits are accepted explicitly.
3. Exact proposed bytes, expected response, shared-access behavior, three-second budget, one-request/no-retry/no-poll limits, cancellation/containment design and sanitized outcomes are reviewable. All unresolved safety-critical fields block sending.
4. Owner explicitly authorizes **separate Stage 3B implementation** and the exact experiment scope. Add synthetic parser/validation/timeout/cleanup tests at that point; no hardware needed. Implementation authorization does not by itself authorize execution.
5. After reviewing that implementation, owner explicitly approves one physical attempt and its exact command, including whether G HUB is open. Stop for owner output after it; any additional attempt needs fresh approval.

No active implementation or physical attempt is authorized by this Stage 3A briefing. Production integration, decoding percentages, retries/polling and sustained coexistence each require further evidence and approval.

## Stage 3A.1 evidence matrix

Stage 3A.1 implements the [passive metadata extension](G305_STAGE3A1_PASSIVE_REPORTIDS.md). The Stage 3A.2 briefing supplies owner-verified Windows results, exit 0. This establishes cached metadata only, not active authorization. **Stage 3B remains NO-GO.**

| Evidence | Current state after implementation |
|---|---|
| Exact USB hardware/collection lengths and usages | Owner-supplied observations retained; G915 excluded |
| Input/output capability ReportIDs | Owner verified input/output button IDs: COL01 0x10, COL02 0x11, COL03 0x20/0x21; absent IDs remain unknown; not exhaustive descriptor evidence; COL03 unexplained/out of scope |
| Anonymous USB MI_02 grouping | Owner verified all three collections under P1 in that invocation; no wireless identity, stable ID or slot inferred |
| Protocol family/version | UNRESOLVED; sizes or IDs alone cannot establish it |
| Mouse device index/route and live identity | UNRESOLVED; receiver present is insufficient |
| Battery feature/register/function and runtime index | UNRESOLVED; no request or decoder selected |
| Battery precision/units/meaning | UNRESOLVED; G HUB bars not an exact percentage |
| Response matching/ownership and G HUB coexistence | UNRESOLVED; passive shared-open says nothing about consuming reports or unique active correlation |
| Exact complete packet and safe active transport | NOT APPROVED / NOT IMPLEMENTED |

## Stage 3A.2 decision

Owner results resolve the cached ReportID and per-invocation parent-group gaps, but not version, mouse route, physical identity, battery semantics or ownership. Logitech documents receiver index 0xFF and root function 1 GetProtocolVersion in general; a receiver answer would not identify a live G305. No wireless slot/software ID/reserved parameters or complete legacy error decoder is selected. **No exact executable packet is proposed.** See [Stage 3A.2 review](G305_STAGE3A2_IDENTITY_PROTOCOL_REVIEW.md) for byte-by-byte unresolved fields and source tables.

Next: obtain accessible primary protocol routing/error documents for offline review. The historical PDFs remain inaccessible here. No new hardware command, sender or capture is authorized. Explicit implementation approval and separate physical-run approval remain mandatory after an exact proposal becomes reviewable.
