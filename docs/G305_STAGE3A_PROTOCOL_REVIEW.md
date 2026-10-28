# G305 Stage 3A — passive protocol review

Date: 2026-10-28. Scope: protocol preparation only. No executable sender, parser, report capture, discovery exchange or production change is introduced. Stage 3B is **NO-GO** pending the evidence and approval gates in [the proposal](G305_STAGE3B_EXPERIMENT_PROPOSAL.md).

## Evidence and provenance

The owner supplied these observations; Codex has not performed Windows or hardware verification:

| Receiver collection | Usage page:usage | Input/output/feature maximum bytes |
|---|---|---|
| 046D:C53F, MI_02, COL01 | FF00:0001 | 7/7/0 |
| 046D:C53F, MI_02, COL02 | FF00:0002 | 20/20/0 |
| 046D:C53F, MI_02, COL03 | FF00:0004 | 32/32/0 |

The distinct G915 receiver **046D:C547 is excluded**. Collection numbering identifies Windows top-level HID collections, not radio slots, paired mice, report IDs or battery endpoints. Sharing a USB parent interface does not make collection handles interchangeable.

The owner's Stage 2 result was Success, exit 0: SQLite opened; data(_id,file) supported; one latest blob parsed; no other rows; 16 candidate references; no structurally associated G305 battery field; no attributable percentage or qualitative level; no conflicts or limit; no SQLite error. Battery/g915/percentage and warning were G915-only. baseModelId, modelId, signature and slotId are model/configuration references. This establishes absence of an attributable value **in that inspected record**, not absence of all telemetry sources. G HUB bars do not establish a percentage. Preserve G305 Unknown in production.

## Sources actually reviewed

Reviewed on 2026-10-28, manufacturer protocol text and Microsoft API documentation only; no third-party monitoring code or proprietary binaries were consulted/copied. References to `master`/`staging` are mutable, not pinned specifications.

1. Logitech [HID++ 2.0 README](https://github.com/Logitech/cpg-docs/blob/master/hidpp20/README.rst): “Packet Structure,” tables 1–5; Device Index, Feature Index, Function, Software ID; “Feature List / 01 - Common.” Retrieved directly from raw.githubusercontent.com/Logitech/cpg-docs/master/hidpp20/README.rst. Documents short 0x10/7 and long 0x11/20, receiver/corded index 0xFF, required root index 0, dynamic feature tables, software-ID echo, feature 0x1000 Battery Unified Level Status and 0x1001 Battery Voltage. A feature-list name does not establish support or wire semantics on this mouse.
2. Logitech [IRoot 0x0000](https://github.com/Logitech/cpg-docs/blob/master/hidpp20/features/0x0000-IRoot.rst): functions table 1; GetFeature arguments/returns tables 3–6; GetProtocolVersion arguments/returns tables 7–9. GetFeature takes feature-ID MSB/LSB and returns an index, type and version. GetProtocolVersion has optional echoed pingData. The protocolMajor table labels 0x8F HID++ 1.0; that label is **not** treated here as an independently verified HID++ 1.0 version reply/decoder. Exact legacy error interpretation remains unresolved.
3. Microsoft [HIDP_CAPS](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/hidpi/ns-hidpi-_hidp_caps), InputReportByteLength/OutputReportByteLength/FeatureReportByteLength; [HIDP_VALUE_CAPS](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/hidpi/ns-hidpi-_hidp_value_caps), ReportID. Reviewed corresponding MicrosoftDocs/windows-driver-docs-ddi staging Markdown. Maximum report buffers include the report-ID byte; zero is used when IDs are not used. Value capabilities identify the report containing each usage/range.

Requested historical manufacturer documents hosted by a third party:

- [HID++ 1.0 specification for Unifying receivers](https://lekensteyn.nl/files/logitech/logitech_hidpp10_specification_for_Unifying_Receivers.pdf).
- [HID++ 2.0 draft, 2012-06-04](https://lekensteyn.nl/files/logitech/logitech_hidpp_2.0_specification_draft_2012-06-04.pdf).

Retrieval was unsuccessful in this environment (initial PDF request HTTP 403; subsequent attempts failed to retrieve either PDF). Their contents/section numbers were **not verified**, so no exact register, legacy error layout or battery function is justified from them. Source gaps are blockers, not grounds to borrow a third-party implementation. An accessible copy of these public specifications would help document general rules, but still would not prove G305-specific routing or support.

## Lengths, report IDs and passive metadata

Windows caps report maximum report size per direction for a top-level collection, including the ID position; they are not a list of actual reports. Do not add another byte to 7 or 20, nor subtract the first byte and assume it is payload. Matching the documented HID++ sizes makes COL01/COL02 plausible transport candidates only. The 32-byte COL03 purpose remains unknown; do not probe it or truncate/pad a short/long message onto it.

Existing diagnose-g305.ps1 uses SetupAPI enumeration, exact hardware filtering, zero desired access, FILE_SHARE_READ|FILE_SHARE_WRITE and HidD_GetPreparsedData/HidP_GetCaps. It returns lengths/counts, not IDs. A future minimal metadata extension could call HidP_GetButtonCaps/HidP_GetValueCaps separately for input/output on already obtained preparsed data and report distinct ReportID values with collection/direction. These are descriptor parsers, not live input sampling. Keep zero-access shared opens and numeric sanitized errors; stop if access fails. Do not request a live descriptor through DeviceIoControl or substitute report reads.

Button/value caps describe represented usages and may not reveal every report/form or a useful vendor payload. Empty capability arrays or IDs absent from them are inconclusive. Even observed 0x10/0x11 IDs cannot prove HID++ version, device route, feature support, a live paired mouse, exclusive response ownership or freshness. No such extension is implemented in Stage 3A: the smallest follow-up scope is described in the proposal.

## Protocol families and battery meaning

| Topic | HID++ 1.0 | HID++ 2.0 |
|---|---|---|
| Model | Legacy register-based operations; precise opcodes/registers unverified here | Feature ID resolved to a device-dependent runtime feature index, then feature function |
| Discovery | Receiver/routing and register semantics require applicable primary spec; Unifying applicability to C53F unproved | Root index 0/GetProtocolVersion and GetFeature are documented general mechanisms, but performing them is active I/O |
| Battery | No register or decoder selected; do not assume a legacy battery register applies | 0x1000 is named Battery Unified Level Status; not runtime index 0x10 or 0x00; exact supported version/function/parameters/response still unverified |
| Other candidates | None selected | 0x1001 is manufacturer-listed Battery Voltage; voltage-to-capacity curve, chemistry and actual support unknown; no further feature inferred |

The 7/20 framing overlap does not discriminate generations. Do not blindly send a 2.0 request and reinterpret any legacy error as battery data. No battery function number or return layout is selected from a feature name alone. A reported percentage can be coarse/quantized rather than an accurate state-of-charge measurement; discrete low/medium/high/full remains qualitative. Voltage cannot be converted to percent without verified device-specific semantics. G HUB bars and a single matching value cannot establish precision, freshness or ownership.

## Identity and coexistence

Proposed identity chain: exact allowed USB receiver instance → its MI_02 collection with verified descriptor ID/direction → independently justified routed wireless slot and G305 identity → live, correlated device response. None of the arrows after collection metadata is established. Retain parent identity only in-memory; sanitized labels must not merge two C53F receivers. Multiple candidates mean stop, not choose the first. G915 VID/PID exclusion precedes any handle open.

Do not equate receiver present with mouse awake/connected. Unknown routing, asleep/offline mouse, unplug/replug or stale buffered traffic yields Unknown; “offline” requires specific verified evidence, not merely a timeout. Future documented read-only receiver identity discovery would itself require a separately approved active exchange. No brute-force device-index scan, pairing modification, wake sequence or automatic discovery is proposed.

G HUB may deny a shared handle or generate overlapping traffic. FILE_SHARE_READ|FILE_SHARE_WRITE is necessary but not a promise: sharing permission is checked against both opens. Never force exclusive access, elevate, terminate G HUB or change drivers. Software-ID/function/route matching and optional documented echo can reduce collisions; IDs are not a globally allocated lock or unique nonce. Another client, stale response or common request can still match. A candidate cannot be declared fresh or uniquely owned merely because its bytes look right. Input reads may consume or compete for reports; passive metadata success does not establish active coexistence.
