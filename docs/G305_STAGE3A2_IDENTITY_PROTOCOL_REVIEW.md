# G305 Stage 3A.2 — identity and protocol discovery review

2026-10-28. Documentation only; **Stage 3B remains NO-GO**. No request bytes, sender, report reader or decoder is implemented. No physical test was performed by Codex. Owner observations below come from the Stage 3A.2 briefing, not a new cloud diagnostic.

## Decision table

| Evidence / question | Status | Consequence |
|---|---|---|
| 046D:C53F / MI_02, COL01 FF00:0001, 7/7/0 bytes; input/output **button** capability ID 0x10 | Owner-verified cached metadata; diagnostic exit 0 | Short-frame transport candidate, not proof of an operational HID++ channel |
| COL02 FF00:0002, 20/20/0 bytes; input/output **button** capability ID 0x11 | Owner-verified cached metadata | Long-frame candidate; not a protocol version or routed mouse |
| COL03 FF00:0004, 32/32/0 bytes; input/output button IDs 0x20 and 0x21 | Owner-verified metadata; purpose unresolved | Exclude from proposed active research; no mapping/truncation onto short/long frames |
| All three collections share P1 | Owner-verified cached USB-interface grouping in one invocation | Not a radio slot, live mouse or stable cross-run identity |
| G915 046D:C547 | Separate receiver, explicitly excluded | Never select its interfaces; no G915 behavior changes |
| Latest SQLite record: 16 G305 model/config references, no attributable battery value | Owner-verified Stage 2 result | No percentage established; G915 keys cannot be assigned to G305 |
| 0x10/7-byte and 0x11/20-byte framing, receiver/corded index 0xFF | Manufacturer-documented general rules, not device-proven exchanges | Compatible metadata does not prove routed request handling |
| HID++ 2.0 root index 0; GetProtocolVersion function 1; optional ping echo | Documented general semantics, not proven on this receiver/mouse | Potential version-only research purpose, not a selected executable request |
| Wireless mouse index and live physical identity | UNRESOLVED | Do not default to slot 1, use USB MI/COL number as slot, or scan |
| Protocol family and applicable legacy response/error grammar | UNRESOLVED | No automatic 1.0/2.0 fallback or decoder |
| Software ID allocation, request ownership/freshness alongside G HUB | UNRESOLVED | No arbitrary software ID or proof from echo alone |
| Battery feature/register/function, runtime feature index and precision | UNRESOLVED | No battery query or percentage conversion |

Capability ID sets may not cover every descriptor report. Owner evidence is specifically from button capabilities; the illustrative **value** capability example in Stage 3A.1 was not actual hardware output. Metadata-only zero-access success proves neither future read/write access nor active coexistence.

## Primary sources reviewed and provenance

Only public manufacturer protocol documentation was consulted for protocol semantics; no third-party battery-monitor source code, driver implementation or proprietary G HUB binary was used. Retrieved 2026-10-28:

1. Logitech [HID++ 2.0 README](https://github.com/Logitech/cpg-docs/blob/master/hidpp20/README.rst), “Packet Structure,” tables 1–5 and subsections Report ID, Device Index, Feature Index, Function, Software ID, Parameters; “Feature List / 01 - Common.” Direct source: https://raw.githubusercontent.com/Logitech/cpg-docs/master/hidpp20/README.rst . SHA-256 of retrieved bytes: `ddf9d2a1a3d06a1bbb58b2f1c3d5e3e38e0e56678a2e4d9149fa4be477b0e6e5`.
2. Logitech [IRoot 0x0000](https://github.com/Logitech/cpg-docs/blob/master/hidpp20/features/0x0000-IRoot.rst), Functions table 1; GetFeature tables 3–6; GetProtocolVersion tables 7–9. Direct source: https://raw.githubusercontent.com/Logitech/cpg-docs/master/hidpp20/features/0x0000-IRoot.rst . SHA-256: `0136b0508af5831ac22a97f66998db742106a2145c139951022f7d392f0d01ae`.

These URLs use mutable master; hashes identify the text reviewed, not a manufacturer revision guarantee. No external source is bundled into runtime code.

The requested [HID++ 1.0 Unifying specification](https://lekensteyn.nl/files/logitech/logitech_hidpp10_specification_for_Unifying_Receivers.pdf) and [2012-06-04 HID++ 2.0 draft](https://lekensteyn.nl/files/logitech/logitech_hidpp_2.0_specification_draft_2012-06-04.pdf) could not be retrieved: both attempts failed with tunnel HTTP 403 Forbidden. Their sections/content are **not claimed as reviewed**. A manufacturer-authored document mirrored elsewhere is distinct from third-party monitoring source, but provenance and applicability to C53F still require review.

## Receiver-directed versus mouse-directed discovery

The README reserves 0xFF for receivers and corded devices. Thus it justifies a general receiver address, **not** the claim that this receiver implements a particular root function, nor that a reply identifies a live paired G305. A receiver-directed version answer would at most describe the addressed responder's protocol behavior. It must not be labeled mouse version/identity, battery support or connectivity.

A mouse-directed request requires an independently justified wireless device index and association with this physical G305. MI_02 is a USB interface number; COL01/02 are top-level collections; P1 is cached USB ancestry. None supplies that index. No primary source reviewed here supplies a C53F-specific fixed mouse route or single read-only identity command that resolves it. There is no basis to pick 1, iterate possible indices, use the G915 receiver's route, or reinterpret 0xFF as the mouse.

A generic root version/ping can, with an established route and complete response grammar, be a narrowly scoped version experiment. It does not return model identity in its documented success parameters and cannot simultaneously establish receiver-to-mouse mapping. A ping reply could show that some addressed target responded, but not uniquely prove freshness/ownership, pairing or physical mouse identity. No assumption that the mouse is awake is permitted. Silence remains inconclusive, not proof of offline/asleep or unsupported protocol.

Legacy HID++ 1.0 uses register-based semantics; HID++ 2.0 uses per-device feature indices and functions. Shared short/long framing does not choose a family. IRoot's GetProtocolVersion table 9 labels 0x8F “HID++ 1.0,” 0x02 “HID++ 2.0 - legacy devices” and 0x04 “HID++ 2.0.” This isolated table is insufficient to treat a 0x8F byte anywhere as a valid version result: a primary legacy error layout and applicability are needed before distinguishing an error from a successful response. Do not invent that layout, equate an unsupported root request with positive device identification, or issue a fallback query.

GetFeature takes a feature ID (MSB/LSB) and returns a runtime index/type/version. Manufacturer feature **0x1000** is Battery Unified Level Status, not runtime index 0x10/0x00. Even a successful version result would not establish support, function semantics or a battery reading. G HUB bars remain qualitative evidence only.

## Single-exchange design review — incomplete, not a packet

Candidate **purpose only**: learn protocol/version of an independently identified addressed target. Battery and pairing discovery are excluded. Current sources do not justify an exact request plus decoder; do not concatenate the following fields into a payload.

| Byte / component under 2.0 framing | General documented meaning | Decision |
|---|---|---|
| 0, report ID / total length / collection | Short 0x10/7 or long 0x11/20; owner verified matching capability IDs | COL01 short is the smaller candidate; exact active choice/transport not approved; COL02 not an automatic fallback |
| 1, device index | 0xFF receiver/corded; wireless target index needed otherwise | Receiver-only route documented generally, receiver root support unproven; **mouse route UNRESOLVED** |
| 2, feature index | Root index 0 | Documented for 2.0 target, applicability unproven; no battery index selected |
| 3, function and software fields | Root function 1 GetProtocolVersion; software ID echoed according to README | Function purpose documented; **software ID and collision policy UNRESOLVED**; no combined byte emitted |
| 4–5, version-request parameters | IRoot table 7 shows unnamed positions | **UNRESOLVED** reserved-value requirements; no assumed zeros |
| 6, optional pingData | IRoot tables 7–8 say it is echoed | No value selected; finite echo is not a unique transaction or physical identity |
| Additional parameters/padding for long form | General long frame exists | Not selected; no padding fabricated |
| Native transfer path and reply association | Must match exact collection, direction and allowed report lengths | **UNRESOLVED** for this receiver; descriptor sizes alone do not select a write API or reply route |

Documented 2.0 version success parameters are protocolMajor, protocolMinor and echoed pingData (IRoot table 8). A future decoder must additionally validate exact framing, target route, feature/function/software fields and specified reply layout before interpreting them. Legacy error encoding, unsupported command responses and complete cross-family classification remain unresolved; no raw byte-position decoder is approved. A single experiment cannot resolve all those unknowns by sending multiple discovery commands.

## Coexistence and fail-closed outcomes

A later tool would require an exact single C53F USB parent, approved collection/usage/ID/direction, minimum documented access with FILE_SHARE_READ|FILE_SHARE_WRITE and no fallback. Denied sharing/access ends the attempt; no G HUB termination, exclusive open or elevation. A second C53F parent, missing ancestry or identity change means stop before sending, not select first. P1 labels alone must not be persisted as a physical identity. G915 filtering precedes opening.

One outstanding request, at most one output and one bounded input completion, three-second monotonic total exchange budget, no drain/retry/poll/wake/slot scan. Unrelated/malformed/short input, wrong ID/route/function, error without a verified grammar, timeout, disconnect or cancellation yields inconclusive/failure. A timeout does not prove mouse offline or request non-delivery. G HUB might receive/consume replies or send lookalike requests; matching software ID and ping echo reduces ambiguity but does not establish exclusive ownership or freshness. No successful-looking reply alone becomes confirmed telemetry.

Future native cancellation/containment must meet the existing proposal's cleanup gates: cancellation is not completion, pending resources cannot be freed early, unknown helper exit latches unsafe. Nothing in this research demonstrates active transport safety or sustained coexistence.

## Concrete next step and approval boundary

Do **not** rerun the passive metadata command merely to repeat confirmed IDs, capture live reports or implement a sender. The smallest useful non-transmitting step is to obtain accessible copies of the two public manufacturer specifications (or authoritative manufacturer documentation covering the same routing and error tables) for offline review. Review receiver routing, supported read-only identity discovery, complete legacy/root error grammar and reserved-field requirements, then establish whether those rules explicitly apply to C53F/G305. Generic Unifying documentation alone may remain insufficient; request device-specific manufacturer documentation rather than guess.

No new Windows utility or raw G HUB data dump is needed for that step. If those documents still cannot establish a mouse route, keep a mouse-directed exchange blocked. A separately proposed receiver-only experiment must be labeled as such and cannot claim to identify the mouse. Owner must first review a complete exact request/response/error proposal and explicitly authorize implementation, then separately approve one physical execution after reviewing the tool. Stage 3A.2 does not grant either approval. Production integration, battery decoding and polling remain out of scope.
