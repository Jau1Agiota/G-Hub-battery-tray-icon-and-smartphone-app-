# G305 Stage 3A.3 — completed primary specification review

2026-10-28. Both owner-attached PDFs have now been read in full. This replaces the access-blocked review at e065251; earlier HTTP 403 findings remain historical. **Stage 3B remains NO-GO.** No code, hardware access, executable packet or active experiment is proposed.

## Sources and page convention

Page citations below are **physical PDF page numbers, starting at 1**, including title/blank pages; the draft contents sometimes points to the page where a heading begins rather than its full definition. Attachments were extracted with pdftotext -layout; the version and battery-status pages were also rendered and visually checked to verify apparent inconsistencies. PDFs/extracted text are not added to the repository or product.

| Source | Provenance and identifier |
|---|---|
| **L1**, Logitech hidpp 1.0 excerpt for public release | Owner attachment `Logitech_HIDPP_1.0.pdf`, 19 physical pages. SHA-256 `24aca57c431863fbfdba9a2b838a5a4dad59deb90ebe5718a675cdc0bf54642c`. Public reference: https://lekensteyn.nl/files/logitech/logitech_hidpp10_specification_for_Unifying_Receivers.pdf . Read attachment; no claim that blocked URL bytes were independently compared. |
| **L2**, Logitech HID++2.0 Draft Specification, 04 June 2012 | Owner attachment `Logitech_HIDPP_2.0.pdf`, 16 pages. SHA-256 `c3d642248df91ee2d00420c359e4693ea36384b63d308abbc45bb4838919e58b`. Public reference: https://lekensteyn.nl/files/logitech/logitech_hidpp_2.0_specification_draft_2012-06-04.pdf . Draft, not a C53F implementation guarantee. |
| **R**, Logitech public HID++ 2.0 README | [Pinned source](https://github.com/Logitech/cpg-docs/blob/f9107a4e3190d798f9be8c5d596f255f5e5741d2/hidpp20/README.rst), Packet Structure tables 1–5; Device Index, Feature Index, Software ID. Independently read in preceding review. |
| **I**, Logitech public IRoot | [Pinned source](https://github.com/Logitech/cpg-docs/blob/f9107a4e3190d798f9be8c5d596f255f5e5741d2/hidpp20/features/0x0000-IRoot.rst), function table 1, GetFeature tables 3–6, GetProtocolVersion tables 7–9. Cross-check, not authority to silently repair draft contradictions. |

No third-party monitoring source, driver implementation or proprietary G HUB binary consulted. The attachments contain protocol descriptions/examples; those examples are evidence to assess, not instructions to execute.

## Routing and transport

L1 p.3 §2.1 explicitly scopes the excerpt to Unifying receiver **046D:C52B**. C53F is not C52B. L1 §2.2 includes embedded receivers in the term “HID++ device”; a response from a receiver need not come from the mouse.

L1 pp.3–5 §§2.3–2.8 documents vendor-defined HID reports, interrupt transport, fixed short/long forms, one command pending per direction, spontaneous data and a source/destination Device Index. §2.6 lists Unifying device indices 1–6 and receiver/corded/Bluetooth 0xFF; §2.8 gives generic device indices 0x01–0xFE, statically or dynamically assigned by the receiver, with connection messages reporting the assignment. These ranges describe different scopes, not a license to scan 254 slots. Neither provides this C53F mouse's index. A single paired mouse is not proof of slot 1.

L1 p.5 §§2.6–2.7 and p.18 §§6.1–6.2 establish 0x10/7-byte short reports and 0x11/20-byte long reports, with vendor usages FF00:0001/0002. Owner passive metadata agrees: C53F MI_02 COL01 input/output button ID 0x10 at 7/7 bytes, COL02 ID 0x11 at 20/20 bytes, both feature length 0. That confirms descriptor compatibility, not an operational channel or protocol generation. COL03 IDs 0x20/0x21 at 32 bytes remain unexplained and excluded. P1 groups cached USB ancestry only; MI, COL and P1 are not wireless addresses or live identity.

**Response length need not equal request length** (L1 p.3 §2.4). L2 p.2 version table illustrates a short reply, while pp.15–16 transaction example shows a short version request answered with a long report. Therefore a future design cannot assume a COL01-only read will capture every valid reply. Cross-collection reply routing and safe bounded receive strategy remain unverified for C53F. Do not add another read, monitor both streams or change budgets automatically.

L1 p.5 §2.9 describes legacy SubID/register operations and LSB-first register values; L2 p.2 describes feature/functions and defaults to big-endian. Do not apply one family's byte ordering or register map to the other. Register read/write commands in L1 pp.5–7 are descriptive only; no register operation is selected.

## Connection and identity evidence

L1 p.9 §§3.1–3.2:

- Disconnection notification SubID 0x40 carries device index and disconnection type 0x02. A disconnected Unifying device cannot receive host HID++ commands.
- Connection notification SubID 0x41 carries index, protocol type (r0 bits 0–2: 4 = Unifying), device type (r1 low nibble: 2 = mouse), software-present/encryption bits, link status (r1 bit 6: 0 established, 1 not established), reason bit and wireless PID (r2 LSB/r3 MSB). A mouse type is not a unique G305 model identity.
- Both notifications depend on enabled wireless-notification reporting. L1 p.11 §4.1 places that receiver flag in register 0x00, r1 bit 0, default disabled at power-up. Enabling it is a configuration write and is outside this stage. Silence is not evidence of offline state or lack of support.

L1 p.12 §4.2 connection-state register documents a receiver connected-device count, not a unique slot/model mapping. §4.3 pairing/lock changes are explicitly excluded. §4.4 activity polling is excluded. Pairing information/name/serial/register layouts (pp.13–15 §4.5) are C52B-scoped examples, not approved C53F discovery; no serial collection, pairing-register query or slot iteration is proposed. Paired records alone do not establish a live radio link.

L2 p.14 feature 0x1D4B describes a device's power-on-reset broadcast: Status 1 reconnection, Request 1 software reconfiguration needed, Reason 1 power-switch activated (0 means none/unknown as specified). It says notification always enabled, but that does not prove G305 feature support or authorize listening/waking/reconfiguration. Do not confuse it with Unifying receiver 0x41 notifications.

## Version detection and draft discrepancies

L2 pp.1–2 “Protocol Versioning” explains deliberate use of unimplemented legacy RegisterAccessID 0x00 to distinguish generations. Its general version request uses short ID 0x10, target DeviceIndex, root/index byte 0x00, function high nibble 1 plus software low nibble, two **explicit zero parameters**, then software-defined ping data. These two zeros are now source-documented rather than unknown reserved guesses. No target, software ID or echo value is selected here; no assembled request is emitted.

For a documented 2.0 success, p.2 returns the target, root and function/software fields with protocol major/minor and echoed ping data; the illustrated major/minor are 2/0, with a general XX/YY form. L2 p.3 says device index, feature index, function and software identifiers return unchanged. I corroborates function 1 and major/minor/echo parameter meanings. A protocol result describes the addressed responder; a receiver-directed result does not identify a live G305 or its routed index.

For 1.0, version detection is an **invalid-SubID error**, not a success whose version is 0x8F. L1 p.7 §§2.9.9–2.11 specifies a short error: byte 0 report ID 0x10; byte 1 same index; byte 2 error SubID 0x8F; byte 3 offending command SubID; byte 4 offending address (or 0 if inapplicable); byte 5 error code; byte 6 zero. A correlated error code 1 denotes ERR_INVALID_SUBID. Other errors do not prove a protocol generation.

**Unresolved source discrepancy:** L2 p.2 prints the version-request function/software byte with high nibble 1, but its illustrated legacy reply's corresponding address field has high nibble F. L1's echoed-address rule would require the actual offending address, not an unexplained transformation. The same L2 page's ping prose mentions hidden function E and a change from F to E, while the displayed request uses function 1. L2 p.4 even labels the second root signature GetFeature(pingData), unlike I's GetProtocolVersion. These are present in the original PDF, not extraction artifacts. Do not silently normalize them into a decoder, choose E/F, or accept mismatched address bytes. Clarification/corroborating primary documentation remains needed for a strict dual-family classifier.

I table 9's shorthand 0x8F “HID++ 1.0” must be read in light of the actual legacy error envelope; it is not a protocol-major success value. L2 pp.15–16 is a C52B multi-index transaction example, sometimes using software ID 0 despite the normative nonzero rule. It is neither a G305 slot assignment nor a safe one-shot recipe; no scan or example packet is copied into an executable proposal.

## Errors and software IDs

L1 p.7 §2.11 gives hexadecimal codes:

| Code | Meaning |
|---|---|
| 0 | Success/undefined |
| 1 / 2 / 3 | Invalid SubID / address / value |
| 4 / 5 / 6 | Connect fail / too many devices / already exists |
| 7 / 8 / 9 | Busy / unknown device / resource error |
| A / B / C | Request unavailable in current context / invalid parameter value / wrong PIN |
| D–FF | Reserved |

L1 p.17 §§5.4–5.6 narrows receiver meanings: Busy = another downstream message is being handled; UnknownDevice = no paired device at that index; ResourceError = disconnected device cannot receive until user interaction/reconnection. These are documented C52B interpretations, not confirmed C53F states. None authorizes retry, waking or scanning. Timeout alone is less specific still.

L2 p.3 error table uses a **different namespace**: 0 NoError, 1 Unknown, 2 InvalidArgument, 3 OutOfRange, 4 HWError, 5 Logitech internal, 6 InvalidFeatureIndex, 7 InvalidFunctionID, 8 Busy, 9 Unsupported. Busy includes another application's request or full pipe. The attached draft lists these codes but does **not** give a complete general 2.0 error-report wire envelope. No remembered marker/offset layout is supplied as fact. Codes alone cannot make a safe decoder; unrecognized/malformed error input must remain inconclusive.

L2 pp.2–3 defines a 4-bit software identifier, requests in range 1–15; 0 is “Do not use,” distinguishing notifications from responses. Firmware copies the ID without other use; application-specific events can also echo it, while broadcasts are not application-specific. Thus echoed software ID alone is insufficient to classify a response, let alone guarantee exclusive ownership or freshness. No globally enforced ID allocation/locking mechanism is documented. Ping echo is another finite discriminator, not a collision-proof nonce. No software ID or ping value is chosen for G HUB coexistence.

Shared access can fail; another client may consume or generate matching reports. The normal single-outstanding-command rule does not serialize independent applications. Zero-access metadata success does not establish read/write sharing or receive ownership. Never force exclusive access, terminate G HUB, elevate, or treat a stale/ambiguous match as confirmed fresh.

## Battery feature 0x1000 — actual documented semantics

L2 pp.9–10 “Battery Unified Level Status,” [0x1000]BatteryLevelStatus, defines:

| Kind | ID | Meaning |
|---|---|---|
| Function | 0 | GetBatteryLevelStatus |
| Function | 1 | GetBatteryCapability |
| Broadcast event | 0 | BatteryLevelStatusBroadcastEvent, spontaneous status |

These IDs are feature-local function/event identifiers, **not runtime feature indices**. L2 p.4 IRoot GetFeature maps a 16-bit feature ID (MSB/LSB) to a one-based runtime index, 0 if not found; root itself is at index 0. Neither this mapping nor support for feature 0x1000 has been established on G305. No discovery or battery request is proposed.

Status report **parameter** bytes (not whole HID report offsets), L2 p.9:

| Parameter byte | Field | Meaning |
|---|---|---|
| 0 | BatteryDischargeLevel | Current level expressed as percent; **0 means unknown**, not proof of empty battery |
| 1 | BatteryDischargeNextLevel | Next level to be reported on discharge; 0 at lowest level or while charging |
| 2 | BatteryStatus | 0 discharging, 1 recharging, 2 final charge stage, 3 complete, 4 below-optimal charging speed, 5 invalid battery type, 6 thermal error, 7 other charging error; 8–255 invalid |
| 3–15 | N/A | Not extra telemetry |

The same p.9 “SW Response Error Exception” block lists status range 0–6 despite the enum defining 7, and terse “next level > current level” text next to range checks. This is an internal draft inconsistency: do not silently drop charging error 7, bless malformed values, or derive a precise validation algorithm without clarification. No production decoder is added.

GetBatteryCapability (no parameters), pp.9–10, names NumberOfLevels (8-bit, 2–100), Flags (8-bit), NominalBatteryLife (16-bit Duration Type) and BatteryCriticalLevel (8-bit percent, mileage-only). Flags: bit 0 disable battery OSD; bit 1 enable mileage (ignored when fewer than 10 levels); bit 2 rechargeable; bits 3–7 zero. Nominal life is used for mileage; this excerpt does not fully define Duration Type encoding or a complete capability response offset table. Do not invent either from field order alone.

When NumberOfLevels <10 **or mileage is disabled**, reported values map to four categories: 0–10 critical, 11–30 low, 31–80 good, 81–100 full. The document explicitly gives 25% for “low” and 50% for “good.” The status field's 0=unknown rule must not be lost by blindly treating all category-range zeros as critical. A percent-shaped number can therefore encode a coarse category, not accurate measured state of charge. G HUB bars cannot establish G305 precision, level count or support. No exact G305 percentage is established.

The p.10 separate “Battery Levels” table lists named values such as BatteryLow 0x18, BatteryGood 0x38 and BatteryFull 0x58, alongside charging/error constants. It does not explicitly map that table into the three status fields or capability wire layout. Do not reinterpret those constants as percentage bytes or substitute them for the status enum.

## Field review and remaining decision

| Component | Now documented | Still unverified / unresolved for this device |
|---|---|---|
| Report framing | Short 0x10/7, long 0x11/20; passive IDs/usages match | Active submission and cross-collection reply routing; response may be long |
| Target | 0xFF receiver, receiver-assigned wireless index | C53F mouse index, physical G305 association/liveness, applicable receiver behavior |
| Version operation | Root index 0, function 1, nonzero software ID, explicit two zero parameters, echoed ping | Chosen ID/echo and collision policy; applicability; draft legacy-address/ping discrepancies |
| Success | Major/minor/echo and unchanged routing/function/software fields | Ownership/freshness and actual G305 version |
| Error | Legacy short envelope/codes; 2.0 error-code names | Strict version legacy discrepancy resolution; general 2.0 wire envelope and C53F applicability |
| Battery | 0x1000 function/event IDs, status fields, coarse mapping | G305 support/index/level precision; draft validation ambiguities; no battery query |

**NO-GO remains**, despite the full-document access blocker now being resolved. No assembled bytes, software ID, mouse slot, sender or physical-test command is supplied. A receiver-directed version reply would not solve mouse identity. Keep G305 Unknown and preserve all production functionality.

For owner review only: unresolved items are C53F-specific routing/identity evidence, authoritative resolution of the draft contradictions and missing error envelope, and a safe cross-collection/ownership/cleanup design. The existing proposal's three-second total budget, one request maximum, no retries/polling/scans/wakes, fail-closed access/cancellation/cleanup, and separate implementation/execution approvals remain unchanged. This review stops here; it does not initiate or propose an active hardware run.
