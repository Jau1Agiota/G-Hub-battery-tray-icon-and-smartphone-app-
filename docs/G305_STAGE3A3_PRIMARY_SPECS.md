# G305 Stage 3A.3 — primary specification review and access limits

2026-10-28. **Partial primary-source review; full PDF review blocked. Stage 3B NO-GO.** Markdown only. No hardware I/O, sender, report reader, decoder or executable packet. The briefing's statement that PDFs are indexed/readable is owner-supplied evidence; this session has no indexed-web/PDF tool and could not independently access them. The distinction is material to every conclusion below.

## Source ledger

| Source | Access and precise scope |
|---|---|
| [HID++ 1.0 Unifying excerpt, reported 19 pages](https://lekensteyn.nl/files/logitech/logitech_hidpp10_specification_for_Unifying_Receivers.pdf) | Direct retrieval failed with tunnel HTTP 403 Forbidden, also on www hostname. Full text **not read**. Section 2.1 and 2.6–2.11 references below are supplied by the owner's briefing, not independently checked citations. |
| [HID++ 2.0 draft, June 4 2012, reported 16 pages](https://lekensteyn.nl/files/logitech/logitech_hidpp_2.0_specification_draft_2012-06-04.pdf) | Same access failure. Pages 1–3 and existence of the 0x1000 section reported by owner; page contents and battery tables **not independently read**. |
| [Logitech cpg-docs](https://github.com/Logitech/cpg-docs/tree/f9107a4e3190d798f9be8c5d596f255f5e5741d2) | Public manufacturer documentation cloned and read at exact commit f9107a4e3190d798f9be8c5d596f255f5e5741d2. It contains repository README, HID++ 2.0 README and IRoot only; no PDFs or 0x1000 function definition. |
| Manufacturer-linked [public documentation folder](https://drive.google.com/drive/u/0/folders/0BxbRzx7vEV7eWmgwazJ3NUFfQ28) | Link verified in cpg-docs root README. Retrieval also failed with tunnel HTTP 403; no documents read through this alternative. |

GitHub API listing and a search-engine request were unavailable (HTTP 403); no verified indexed excerpts were obtainable. A guessed 0x1000 documentation filename returned 404 and is not evidence. No private mirror, access bypass, third-party monitoring implementation, driver source or proprietary binary was used. Do not turn the owner's supplied section summary into a claim of full specification access.

Independently read manufacturer sources, pinned for review:

- [HID++ 2.0 README](https://github.com/Logitech/cpg-docs/blob/f9107a4e3190d798f9be8c5d596f255f5e5741d2/hidpp20/README.rst): Packet Structure tables 1–5; Device Index, Feature Index, Function, Software ID; Feature List / 01 - Common.
- [IRoot](https://github.com/Logitech/cpg-docs/blob/f9107a4e3190d798f9be8c5d596f255f5e5741d2/hidpp20/features/0x0000-IRoot.rst): Functions table 1, GetFeature tables 3–6, GetProtocolVersion tables 7–9, NoOperation event.

## Routing, connection notifications and legacy errors

The owner's briefing reports that the 1.0 excerpt §2.1 explicitly targets Unifying **C52B**, not this receiver **C53F**. Treat that scope restriction as a warning against importing example slots, connection notifications or receiver commands. Even if a generic short-frame rule agrees with observed C53F metadata, it does not establish that all C52B receiver behavior transfers.

The owner reports §§2.6–2.11 cover short ID 0x10/7 bytes, long ID 0x11/20 bytes, receiver index 0xFF, receiver-assigned device indices, error marker 0x8F and legacy codes. Independently read README corroborates framing and the general receiver/corded 0xFF reservation, but does not establish C53F slot allocation or legacy errors. No wireless slot is selected.

**Connection-notification payloads, enablement requirements, source/target fields and error-code tables remain unverified here.** No exact notification opcode, connection-bit meaning, legacy error payload offsets or code-to-state mapping is documented as established. In particular, no notification subscription/configuration or input listening is proposed. An observed receiver interface cannot substitute for a current mouse connection notification, and cached pairing information would not by itself prove liveness. A timeout cannot distinguish off, asleep, disconnected, unsupported, access conflict or lost response.

A 0x8F marker reported in the owner's legacy summary must not be confused with a protocol version byte in an arbitrary frame. The independently read IRoot table 9 labels 0x8F HID++ 1.0, 0x02 HID++ 2.0 legacy devices and 0x04 HID++ 2.0, but it does not independently supply the full legacy response/error grammar. Until the full draft and legacy tables are read, no cross-family classifier can be justified from those labels alone.

## Version discovery: what the readable source establishes

The 2.0 README places report ID at byte 0, target device index at byte 1, feature index at byte 2, function/software fields at byte 3, and parameters from byte 4. Short messages have three parameter bytes; long messages sixteen. Root feature index is 0. IRoot table 1 identifies function 1 as GetProtocolVersion, function 0 as GetFeature. These general rules are documented, not C53F-proven command execution.

IRoot GetProtocolVersion table 7 places optional pingData in argument position 2; positions 0 and 1 are unnamed. Table 8 returns protocolMajor, protocolMinor and echoed pingData. This is enough to describe success **parameters**, not a complete verified dual-family decoder. The draft's reported pages 1–3 may resolve general request constants, software-ID restrictions and 1.0 versus 2.0 reply/error formats, but those contents were inaccessible here. Do not assume unspecified parameter values, invent reserved-bit rules or copy remembered error layouts.

The README documents software-ID echo to distinguish application replies. It does not provide an exclusive allocation mechanism; this review selects no ID. Even a matching software ID and ping echo is a finite correlation value, not a proof that another G HUB transaction or stale response could not match. G HUB may deny shared access or consume/compete for responses. A passive zero-access open says nothing about later input ownership.

Receiver-directed and mouse-directed requests have different claims. A response addressed to receiver index 0xFF could at most characterize that responder under verified semantics; it does not identify a live G305, its routed index or its protocol. A mouse version experiment needs independently justified receiver-to-mouse routing. USB MI_02, COL01/02 and anonymous P1 do not provide it. No index scanning or “default slot 1” is defensible from present evidence.

## Battery feature 0x1000: review explicitly incomplete

The readable manufacturer feature list establishes only the name **Battery Unified Level Status** for feature ID 0x1000. IRoot GetFeature tables 3–4 establish the distinction between a two-byte feature ID argument and returned per-device runtime feature index/type/version. Feature 0x1000 is not runtime index 0x10 or 0x00.

**Actual 0x1000 function IDs, response fields, units, status/category enums, capabilities and precision are NOT established in this review.** The requested draft section could not be read, and the public cpg-docs tree has no equivalent definition. Do not infer a function number or fields from the feature title or remembered implementations. Reading and citing that full section remains an outstanding deliverable, not a completed finding.

A protocol's percentage field, if later verified, must still be distinguished from a discrete level/category and from accurate measured state of charge. It could be coarse or device-estimated. G HUB bars cannot be converted into exact percentage, and no G305 support for 0x1000 is established. A first experiment would be identity/version-only, never battery discovery/query.

## Field-by-field candidate purpose, not executable packet

| Component | Evidence | Decision |
|---|---|---|
| Report ID/collection/length | Owner passive COL01 input/output button ID 0x10/7; COL02 0x11/20; manufacturer general framing agrees | COL01 is the smaller candidate; no active transport selected/approved; COL02 is not a fallback |
| Device index | Manufacturer reserves 0xFF for receiver/corded; owner reports legacy index assignment by receiver | **Mouse index UNRESOLVED**; receiver-only target cannot claim mouse identity |
| Family / operation | General 2.0 root feature index 0, function 1 documented | Applicability to chosen target **UNRESOLVED**; no legacy register operation selected |
| Combined function/software field | Manufacturer describes fields and software echo | Software ID/rules/collision policy **UNRESOLVED**; no combined byte emitted |
| Parameters / padding | Root table 7 names third argument pingData, first two unnamed | Required values/reserved fields **UNRESOLVED**; no chosen echo/padding |
| Success decoding | Root table 8 names major/minor/echo | Full response headers and cross-family classification **UNRESOLVED** |
| Errors | Owner reports legacy 0x8F; full draft general errors not read | Exact layouts/codes **UNRESOLVED**, no guessed decoder |
| Ownership/liveness | No exclusive allocation or verified mouse route | **UNRESOLVED**, response could remain ambiguous |

Owner-passive COL03 0x20/0x21 at 32 bytes remains unexplained and excluded. All three collections under P1 establishes shared cached USB ancestry only. Known descriptor IDs resolve a metadata gap, not the routing/error/identity gaps. No contiguous packet, byte array or physical command is emitted because safety-critical fields remain unresolved.

## Decision and smallest next step

**NO-GO for Stage 3B implementation or execution.** First obtain the two public PDFs as attachments (or an accessible authoritative copy) so their full text, diagrams and exact tables can actually be reviewed offline. No extra hardware diagnostic is needed for this access blocker. Preserve provenance; do not supply private captures, G HUB databases or proprietary binary dumps. A full review must then resolve general routing/notification/error semantics and 0x1000 fields, while separately checking C53F applicability. It may still leave the mouse index unknown.

Any future proposal must state one identity/version-only purpose, exact complete request, applicable success/error decoder, receiver versus mouse claims, software/echo collision limits and a single allowed target. Minimum shared access, one outstanding request, one output/one bounded input completion, three-second total exchange deadline, no retry/poll/scan/wake, safe cancellation and verified cleanup remain required. Access denial, a second C53F parent, identity change, mismatched input, timeout, cancellation or unknown cleanup must fail closed; G915 remains excluded. Never force G HUB closure or privilege changes.

Only after those gaps are resolved may the owner separately authorize implementation, review its synthetic/native safety evidence, and then separately approve one physical run. This stage authorizes none of those actions and no production integration.
