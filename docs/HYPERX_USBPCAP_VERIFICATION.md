# Stage 4.0C: verify the POC's USB transport

## Current evidence and scope

On 2026-10-27 the owner reported `CandidateReceived`, 56%, response prefix `66 89 0E E4 38` followed by zeros; NGENUITY opened afterward also showed 56%. Separately, TShark inspection of frame 9 in the original `hyperx-battery-66.pcap` verified the reference request: 62 bytes, `66 89` plus 60 zeros, SET_REPORT 21/09, wValue 0266, wIndex 0003, wLength 62.

This procedure checks whether the **unchanged isolated POC** generates that same wire transfer. It does not authorize initialization commands, new payloads, retries, polling or production integration. One physical command is issued, then its result and capture are reviewed before any further attempt.

## Owner-reported POC wire verification (2026-10-27)

Capture `Hyperx.pcapng`, USBPcap2, bus 2, device address 12 (capture-local identifiers, not selectors):

| Frame | Owner-observed transfer |
| --- | --- |
| 10061 | Control OUT endpoint 0x00, USB SUCCESS; SET_REPORT bmRequestType 0x21, bRequest 0x09, wValue 0x0266, wIndex 3, wLength 62; body exactly 66 89 followed by 60 zero bytes |
| 10062 | Interrupt IN endpoint 0x84, USB SUCCESS; 62-byte response beginning 66 89 0E E3 38, matching the POC response exactly; byte index 4 = 0x38 = 56% |
| 10063 | Zero-length interrupt IN URB, USB SUCCESS; no battery data; not a second battery sample or evidence of an extra application read |

The owner reports successful on-wire verification. The setup fields and complete output match the reference request; the input matches the POC result. The earlier independent NGENUITY comparison also showed 56%. This evidence verifies the observed POC transport on this hardware, not all states or concurrency. The cloud task has not independently opened the raw capture. Frame adjacency alone does not establish URB relationships; no interpretation of frame 10063 beyond its reported zero-length successful IN is assigned. No additional request-count or URB-pair details are inferred beyond the supplied observations.

Next: [controlled NGENUITY coexistence evaluation](HYPERX_NGENUITY_COEXISTENCE.md). Production integration remains deferred. The procedure below is retained as a repeatable reference, not an instruction to repeat the completed test.

## Prepare capture without sending a request

1. Close NGENUITY and its background helper manually. Leave them closed throughout capture. Use one matching receiver and no other active POC process. Do not change audio settings, firmware or drivers.
2. In Wireshark's USBPcap capture-interface details, identify the current root hub/device for VID 03F0, PID 05B7. Verify current enumeration rather than assuming the earlier USBPcap2 / address 20 assignment. Record the current bus and device address locally; USB addresses may change after reconnection.
3. Select only that root hub for a short capture. If identification is uncertain, stop here and resolve it before sending anything. Capture files can contain unrelated USB traffic and device identifiers; keep them local and outside version control (capture files are gitignored).
4. Start capture before launching the POC. Do not apply a restrictive capture filter that might discard control transfers or their data/completions. Display filtering after capture is safer. If capture setup requires elevated Wireshark access, keep the POC in an ordinary, separate PowerShell window; do not elevate the POC or alter driver permissions.

## One controlled request

From the repository root, on the unchanged Stage 4.0C branch, execute this command **once**:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\hyperx-battery-poc.ps1 -ReadBattery -ConfirmNgenuityClosed -CopySummary
```

The POC attempts at most one fixed output report and one input report with a shared three-second exchange deadline after metadata/open setup. This is not a three-second bound on restore/build, metadata calls or a defective driver. There are no automatic retries or polling. Record the copied summary and launch/finish times; the percentage may differ from 56% as the battery changes.

Stop capture promptly after the process finishes, including on timeout/error. Save the capture locally. **Do not rerun on failure.** Cancellation/timeout does not prove the request was never delivered. Paste the summary and sanitized packet findings for review before another request. Only after capture stops, reopen NGENUITY to record the independent percentage and the approximate comparison delay. Do not attribute its subsequent traffic to the POC.

## Inspect the saved capture

Use Wireshark display filters with the actual current values, for example `usb.bus_id == B && usb.device_address == D` (replace B/D with integers). Confirm the receiver identity using enumeration/capture-interface information, not its transient address alone. Expand USB setup and data fields; do not mistake total captured frame length for report length. TShark can inspect the same saved capture without opening the device; field availability varies with dissector/version, so use verbose packet details if a convenience field is unavailable.

Check the control URB submission and its matching completion by URB ID and time. A submission/completion pair represents one transfer, not two requests. Check input submissions/completions similarly; the pre-armed input read may have been submitted before output. Record the completed input data after the output, not merely the time the read was armed.

| Evidence | Required observation |
| --- | --- |
| Output direction/type | Host-to-device, class/interface control SET_REPORT |
| bmRequestType / bRequest | 0x21 / 0x09 |
| wValue | 0x0266: output report type 2, report ID 0x66 |
| wIndex / wLength | 0x0003 / 62 (0x003E) |
| Complete output data stage | Exactly 62 bytes: 66 89 followed by exactly 60 zero bytes; no dropped or duplicated report ID |
| Output completion | Successful USB/URB completion; record any failure status |
| Completed input | Interrupt IN, expected observed endpoint 0x84; record actual endpoint, completion status and full data length |
| Response contents | Exactly 62 bytes, prefix 66 89, byte 4 in 0–100 and equal to the POC's displayed candidate; compare the entire report against the POC summary |
| Request count | Exactly one matching 66/89 SET_REPORT transfer in the bounded POC window, accounting for submission/completion pairs |
| Other commands | No POC-attributed 8F/82/84/85/86/8A/83 initialization or feature requests |

Do not infer process attribution from an address/filter alone: USBPcap is not a process trace. NGENUITY/helper closure, a bounded execution window, one POC invocation and matching payload/timing provide controlled evidence. Unrelated Windows USB traffic may still occur. If unexpected commands appear, preserve and investigate them rather than assuming they are POC traffic or silently excluding them. A reused endpoint or ambiguous capture is not sufficient to pass verification.

Bytes 2/3 remain unknown. Do not assign counter, charging or connectivity meanings. A single correlated response and matching NGENUITY value do not prove freshness in headset-off/disconnected cases.

## Share a sanitized verification record

Report the POC commit, Windows/tool versions, result/exit status, request submission/completion frame numbers and statuses, setup fields, complete 62-byte request, input completion frame/endpoint/status/length and complete bounded response, plus NGENUITY's subsequent percentage and comparison delay. State whether exactly one matching transfer appeared and whether unexpected traffic was present. Omit full HID paths, USB serials, instance identifiers, usernames and unrelated device traffic. Do not commit or publish the raw capture; retain it locally for targeted review.

Transport passes only after the exact setup/data fields, successful completions, one-request count and corresponding input/POC summary are reviewed. A successful battery result alone does not complete this gate. The owner-reported result above is distinct from this repeatable procedure.

## Deferred integration planning

After transport verification, separately design and review a provider with conservative polling and no overlapping requests; explicit unavailable/stale/disconnected states rather than a fabricated percentage; bounded cancellation, reconnection and safe handle/buffer ownership for a long-running process. Establish polling intervals from measured behavior rather than guessing. Validate headset off, receiver absent/unplugged, cancellation, malformed responses, repeated runs/resource cleanup, audio continuity and NGENUITY coexistence before production integration. Preserve the isolated POC as a diagnostic reference. No such implementation is included in this documentation update.
