# G305 passive HID++ feasibility — Stage 2

This is a documentation-only assessment, not a request builder, capture tool or production provider. Owner-supplied metadata in the Stage 2 briefing describes receiver **046D:C53F / MI_02**:

| Collection | Usage page:ID | Input / output / feature bytes |
|---|---|---|
| COL01 | FF00:0001 | 7 / 7 / 0 |
| COL02 | FF00:0002 | 20 / 20 / 0 |
| COL03 | FF00:0004 | 32 / 32 / 0 |

These are owner observations, not new cloud hardware verification. The separate **046D:C547** G915 receiver remains excluded by the existing passive G305 inspector's exact filters. No new HID handles or imports are added in this stage.

## Why the lengths are plausible, but insufficient

Logitech's own [HID++ 2.0 public protocol documentation](https://github.com/Logitech/cpg-docs/blob/master/hidpp20/README.rst) describes short 7-byte and long 20-byte message forms, including report IDs and routing/feature/function/software fields. The independently reported 7/20 lengths are therefore plausible HID++ transport candidates. **They do not establish** actual descriptor report IDs, protocol generation/version, receiver-to-mouse routing, device/feature indices, supported battery features, freshness, a qualitative-versus-percentage meaning, or ownership of an input response. The 32-byte collection does not match those two documented forms; no battery purpose is inferred from it.

Protocol documentation says report types are determined by the descriptor and feature indices are device-dependent. No generic example is a verified request for this user's receiver. No report ID, device index, feature index, packet or payload is selected/constructed here. The software-ID concept can support future correlation, but merely choosing a value does not prove exclusive ownership alongside G HUB.

Only this manufacturer's protocol documentation was consulted; no third-party G HUB battery-monitor implementation or driver source was read/copied. All diagnostic code is original.

## Existing passive capability coverage

`diagnose-g305.ps1` already reports paths sanitized to fixed hardware tokens, usage page/ID, input/output/feature lengths, link collection count and button/value/data-index capability counts. It opens with zero desired access, read/write sharing, and uses preparsed-data/capability metadata. That is sufficient for this first feasibility comparison. It does not identify a physical mouse behind a receiver, decode packets, prove feature support or group device ownership from report size alone.

No passive inspector change is necessary before the owner returns the SQLite result. If further descriptor evidence is needed, a separately scoped metadata-only extension could examine cached preparsed button/value-capability report-ID metadata while retaining zero-access/shared-open and sanitized output. It must not turn into input sampling, feature reads, USB packet probing or a discovery query. Any missing metadata remains a documented gap, not an invitation to send a request.

## Evidence needed before proposing an active experiment

1. Receive the new sanitized SQLite/JSON report, including errors/limits and additional-row indication. A no-finding latest-row report is inconclusive, not proof that no G HUB source exists. Decide whether a narrowly scoped further read-only schema investigation is justified.
2. Review the existing passive capability report and trustworthy descriptor evidence. Confirm the exact collection/descriptor format and receiver/mouse association. Do not assume G305's protocol version or routed identity from C53F or 7/20-byte lengths alone.
3. Establish candidate battery-feature semantics from public manufacturer documentation and device-specific evidence. G HUB reportedly shows qualitative battery bars: a future unknown/low/medium/high result must not become an invented exact percentage.
4. Write a concrete proposal documenting every justified field, minimum shared access, one-flight isolation, process/handle ownership, bounded cancellation/exit handling and maximum one exchange. No code should build or transmit that packet as part of Stage 2.
5. **Explicit owner approval checkpoint:** owner reviews the exact proposed request, purpose, ambiguity/coexistence risks, deadline and cleanup strategy before a separate active POC branch or any hardware execution. Approval for this SQLite diagnostic or the earlier HyperX POC does not authorize a G305 packet.
6. Only after separately authorized implementation/review may the owner perform the controlled test and return sanitized results. Do not automatically close G HUB, change drivers/services, elevate, retry/poll or sample arbitrary reports. Account for competing G HUB traffic, buffered/stale replies, routing ambiguity, sleeping/off mouse, disconnects and timeouts; fail conservatively. Production integration requires its own verified semantics and approval.

Current conclusion: a passive metadata match makes further investigation reasonable, but **no G305 battery channel or percentage decoder is confirmed**. Await owner evidence; no active HID communication is implemented or authorized here.
