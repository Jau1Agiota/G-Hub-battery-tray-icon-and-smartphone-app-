# Stage 4.0C: isolated active HyperX battery proof of concept

**Owner-reported physical validation succeeded: the isolated POC returned 56%, matching NGENUITY opened afterward. The owner subsequently verified the exact POC on-wire request and response; coexistence remains pending.** The default remains passive inspection. Production battery monitoring, G305/G915 providers, tray UI and polling are unchanged.

## Sources reviewed and evidence limits

The owner supplied the current Windows temporary `HyperXBatteryPOC` Program.cs and csproj. They implement a metadata-only .NET 10 inspector: explicit SetupDiGetDeviceInterfaceDetailW, Unicode path at offset 4, zero-access shared handles and preparsed/capability queries. They were reviewed before implementation. The repository's existing inspector already preserves that approach with SafeHandle ownership and stricter filtering. Neither temporary project, Program.Inspector.Backup.txt nor the earlier temporary lab was modified, assumed integrated, or copied wholesale into the repository.

The owner verified `03F0:05B7 / MI_03 / COL01`, usage `FF13:0001`, input/output reports 62 bytes, feature length 0. COL02/03 are not battery communication targets. Supplied NGENUITY 2.34.0.0 / Wireshark 4.6.9 / USBPcap 1.5.4.0 evidence:

| Reference shown in NGENUITY | Supplied input prefix | Candidate byte 4 |
| --- | --- | --- |
| 64% | 66 89 0F 1B 40 00 ... | 0x40 = 64 |
| 66% | 66 89 0F 27 42 00 ... | 0x42 = 66 |

The second capture explicitly matched the `66 89` outgoing request and incoming response. This strongly supports byte 4 as a battery candidate but does not characterize the complete protocol, off/disconnected behavior or freshness. Bytes 2 and 3 remain unknown; no counter/charging/status meaning is assigned. Earlier passive windows with/without NGENUITY yielded no unsolicited reports, but do not prove such reports never occur.

## Exact proposed request: review before any active test

62 bytes **including** report ID. Byte 0 is `66`, byte 1 is `89`; bytes 2 through 61 are zero:

```text
66 89 00 00 00 00 00 00 00 00 00 00 00 00 00 00
00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00
00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00
00 00 00 00 00 00 00 00 00 00 00 00 00 00
```

**Payload verified by the owner:** TShark inspection of frame 9 in the original `hyperx-battery-66.pcap` confirmed all 62 bytes: `66 89` followed by exactly 60 zero bytes. The original SET_REPORT setup fields were 21/09, wValue 0266, wIndex 0003, wLength 62. This verifies the reference request, not the transfer emitted by the POC; that separate capture remains pending.

The only output buffer builder creates this exact packet afresh. There is no parameter for report IDs, commands, arbitrary bytes, VID/PID, endpoint, device address or timeout. Other observed initialization commands 8F/82/84/85/86/8A/83 are not sent. No feature request, initializer, retry, flush, queue drain or polling loop is implemented.

## First command: payload preview only

On this feature branch:

```powershell
.\hyperx-battery-poc.ps1 -PreviewRequest -CopySummary
```

This restores/builds only the isolated project and copies the exact preview. It does not enumerate/open a HID interface or send/receive reports. Paste the result for review, and wait before the first hardware-test command. If local execution policy blocks scripts, invoke the same script through a process-only `powershell.exe -NoProfile -ExecutionPolicy Bypass -File` prefix; no machine policy is changed.

Without mode switches, the wrapper/project runs passive HyperX inspection. `diagnose-hyperx.ps1`, `diagnose-g305.ps1`, the original inspector executable and their defaults remain unchanged as fallbacks.

Active mode exists only in the new project and requires **both** `--read-battery` and `--confirm-ngenuity-closed`; its wrapper requires the corresponding `ReadBattery` and `ConfirmNgenuityClosed` switches together. A lone read flag is rejected without opening a device. The second flag is an explicit manual acknowledgement, not proof: visible process names containing NGENUITY are also checked, but an unnamed/helper process cannot be conclusively ruled out by that heuristic. The utility never closes/kills NGENUITY itself. No active ready-to-run command is supplied at this review step; hardware commands are handed off one at a time after the preview is reviewed.

## Transport, access and lifetime

The new `PeripheralBatteryCompanion.HyperXBatteryPoc` console project depends on the existing independent inspector, not the tray app. The only inspector project change grants access to its existing internal metadata interop for reuse; its native import whitelist stays unchanged. No new package dependency is added.

Before sending, the POC:

1. Refuses incomplete enumeration, zero matching COL01 interfaces, or multiple matching receivers. Exact VID/PID/MI/COL filtering is case-insensitive; COL02/03 and Logitech receivers cannot be selected.
2. Opens COL01 with zero access to inspect capabilities, then requires `FF13:0001`, input/output 62, feature 0.
3. Opens only the selected path with **GENERIC_READ**, **FILE_SHARE_READ | FILE_SHARE_WRITE**, **OPEN_EXISTING**, **FILE_FLAG_OVERLAPPED**. GENERIC_READ is needed for input; the output IOCTL is FILE_ANY_ACCESS, so GENERIC_WRITE is not requested. There is no rights escalation or broader-access fallback. Capabilities are rechecked on the actual read handle.
4. Uses a per-Windows-session named semaphore (safe to release after an async continuation) to reject concurrent active POC runs. It checks NGENUITY before opening an active session and never changes audio/device/driver settings.
5. Arms one overlapped input read and waits for its native submission before output. Input already completed before the output attempt causes refusal, with no deliberate resend/drain. The single response is validated, not streamed or logged indefinitely.

The output operation is **DeviceIoControl(IOCTL_HID_SET_OUTPUT_REPORT = 0x000B0195)** with a 62-byte input buffer and no output buffer, on an overlapped handle. The constant follows the official HID header: FILE_DEVICE_KEYBOARD 0xB, function 101, METHOD_IN_DIRECT 1, FILE_ANY_ACCESS 0. This selects the HID class output-report operation rather than assuming WriteFile/IRP_MJ_WRITE uses the captured control-transfer path. The synchronous HidD_SetOutputReport wrapper is deliberately not used for the timed exchange. This low-level user-mode IOCTL route may be rejected by a driver; rejection is reported without falling back or retransmitting.

**Physical wire mapping was subsequently verified by the owner; see the dated update below.** The original verification criteria were: Microsoft documents the output-report operation and its report-ID convention; that does not prove the exact bytes/setup fields emitted by this receiver driver. The USB capture checks are:

- bmRequestType 0x21, bRequest 0x09 (SET_REPORT), wValue 0x0266 (output/report ID 66), wIndex 3, wLength 62.
- Data exactly matching the full proposed packet above, including ID/padding convention.
- The matching incoming report through the observed interrupt IN endpoint 0x84. ReadFile uses the HID class handle; no endpoint number or USBPcap address is hardcoded. The supplied address 20 and root-hub assignment were temporary observations, not selectors.

Exactly one input and one output are permitted by the native session. Both operations share a **three-second exchange deadline**, started after metadata/handle setup, not three seconds each. No command-line timeout override exists. Native submission/cancellation run on workers; tasks can time out/cancel without waiting indefinitely in GetOverlappedResult. ThreadPoolBoundHandle/IOCP owns pinned buffers and OVERLAPPED storage until native completion **and** API return, with an additional lease during CancelIoEx. A timeout never frees memory still in use by Windows. Handles/buffers are released once outstanding native work completes. A non-completing/defective driver may defer reclamation until the standalone process exits; this is an experimental one-shot design, not a production provider. The deadline is not a hard real-time guarantee for unrelated synchronous metadata/open/close calls or a broken kernel driver.

Cancellation does not prove non-delivery. A timeout/output error may occur after the device accepted the one request; never rerun blindly. No pending I/O buffer is reused for another request. Ctrl+C requests cancellation. Ordinary native completion releases the native allocation; session disposal cancels any remaining input/output and retains ownership until safe completion.

## Validation and output

Only an exactly 62-byte response with byte 0 `66`, byte 1 `89`, and byte 4 in 0–100 yields a **candidate** percentage. Unknown fields are not constrained or interpreted. Malformed lengths, other IDs/commands and out-of-range values are rejected without displaying/interpreting an invalid report. A validated response alone is displayed as bounded 62-byte hex; there are no file logs or arbitrary input dumps.

A new file handle and pre-armed read reduce some stale-data risks, but matching headers alone cannot prove correlation/freshness. Unknown bytes 2/3 are not used as invented transaction counters. The owner independently confirmed one POC result against NGENUITY (56%); captured POC request/response correlation and wire equivalence still require verification. Headset-off/disconnected status responses might need further characterization even when byte 4 happens to fall in range.

Reports use fixed status names and numeric Win32/HID status codes without interface instance IDs or raw exception messages. Status distinguishes refusal, timeout, cancellation, invalid response and validated candidate. Request-attempted means the coordinator requested submission, not proof of a physical USB write; an additional native gate/cancellation can still refuse it. Output-completed means the API operation completed, not independently verified wire equivalence.

Exit codes: 0 candidate received (or successful passive/preview); 2 unsupported platform/invalid flags/cancellation; 3 no receiver; 4 exchange timeout; 5 ambiguous receivers; 1 other refusal/API/protocol failure. The wrapper preserves build/runtime errors and reports clipboard failure separately.

## Physical-device checklist: one command at a time

**Phase A, completed by the owner:** full reference payload verified; isolated POC returned `CandidateReceived`, 56%, with response prefix `66 89 0E E4 38` followed by zeros. NGENUITY opened after the test also displayed 56%. This is one successful physical comparison, not verification of the POC USB setup fields or disconnected/freshness semantics. Preserve the temporary inspector/backup and ordinary passive launcher as fallbacks.

**Current step:** transport verification succeeded as recorded below; follow the [coexistence evaluation plan](HYPERX_NGENUITY_COEXISTENCE.md). Keep production integration deferred.

**Phase B, only after that review:**

- Manually close NGENUITY and its background helper. Do not change audio routing, volume, lighting or firmware. Attach only one matching receiver; do not elevate.
- Prepare USBPcap using current enumeration to identify the receiver, not the old address 20. Start capture before the single approved command.
- Execute only the one command supplied for this step. The code attempts at most one captured command, with no automatic retry.
- Paste the bounded result/validated response (if any) and review the capture. Confirm all setup/data fields above before calling the transport correct. Note that Windows/other applications can produce unrelated USB traffic; do not attribute it to this program without matching the specific transfer.
- Stop the capture before reopening NGENUITY for the independent percentage comparison. A failed attempt is investigated first; no second transmission is automatic.

**Phase C, later reviewed attempts:** different battery percentage; receiver detach/reconnect; headset off; absent receiver; API/cancellation/timeout outcomes; malformed frames using synthetic tests; normal audio playback; process/handle cleanup. Each real request requires an individually agreed command/result cycle. Do not mutate report fields or probe other commands to make a failure pass.

**Phase D, deferred:** only after physical validation consider extracting a long-lived provider, conservative polling, availability semantics and NGENUITY coexistence. There is no production integration or Android work here.

## Official sources consulted

- [Microsoft HID header](https://github.com/microsoft/win32metadata/blob/main/generation/WinSDK/RecompiledIdlHeaders/shared/hidclass.h), HID_IN_CTL_CODE(101).
- [IOCTL_HID_SET_OUTPUT_REPORT](https://learn.microsoft.com/windows-hardware/drivers/ddi/hidclass/ni-hidclass-ioctl_hid_set_output_report), report-ID buffer convention and zero Information on successful class-driver completion.
- [Sending HID reports](https://learn.microsoft.com/windows-hardware/drivers/hid/sending-hid-reports) / [HidD_SetOutputReport](https://learn.microsoft.com/windows-hardware/drivers/ddi/hidsdi/nf-hidsdi-hidd_setoutputreport), including the documented compatibility caveat for devices that do not support this operation.
- [Obtaining HID reports](https://learn.microsoft.com/windows-hardware/drivers/hid/obtaining-hid-reports), interrupt input via ReadFile.
- [DeviceIoControl](https://learn.microsoft.com/windows/win32/api/ioapiset/nf-ioapiset-deviceiocontrol), [CancelIoEx](https://learn.microsoft.com/windows/win32/fileio/cancelioex-func) and [ThreadPoolBoundHandle](https://learn.microsoft.com/dotnet/api/system.threading.threadpoolboundhandle).

Only the supplied original POC, repository code, SDK header and official documentation were used; no existing G HUB/HyperX battery-monitor project implementation was consulted.

## Owner update: POC wire transport verified (2026-10-27)

The owner reports `Hyperx.pcapng` frame 10061: successful control OUT SET_REPORT 21/09, wValue 0266, wIndex 3, wLength 62, exact body 66 89 + 60 zeros. Frame 10062: successful interrupt IN endpoint 84, 62 bytes beginning 66 89 0E E3 38, matching the POC response and 56% candidate. Frame 10063 is a successful zero-length IN URB, not another battery reading. The earlier NGENUITY comparison also showed 56%.

This supersedes earlier pending-wire statements for this observed hardware exchange; it does not validate concurrency, disconnection or long-running resource lifetime. See the [wire record](HYPERX_USBPCAP_VERIFICATION.md) and [coexistence test plan](HYPERX_NGENUITY_COEXISTENCE.md). Coexistence is the next gate. The existing active mode still requires NGENUITY closed; do not bypass that guard to test concurrency. No executable, launcher or production behavior changes accompany this documentation update.

## Explicit passive shared-access mode

Use `hyperx-battery-poc.ps1 -PassiveCoexistence -CopySummary` with NGENUITY open for the [controlled coexistence test](HYPERX_NGENUITY_COEXISTENCE.md). Unlike preview, it enumerates and inspects exact COL01, then opens/closes a shared GENERIC_READ/overlapped handle without reading or transmitting any report. It does not create an active battery session or relax active safety guards. Report metadata is zero-access; the separate read-access open tests compatibility with the existing active handle requirements. No production provider changes.

## Stage 4.0D: separately acknowledged coexistence experiment

The isolated POC now accepts `-ActiveCoexistence -AcknowledgeCoexistenceRisk` (CLI: `--active-coexistence --acknowledge-coexistence-risk`). This requires NGENUITY observed running and does not weaken normal `-ReadBattery -ConfirmNgenuityClosed`. It reuses the one-request/one-input transport and deadline. Valid concurrent replies are always labeled `CandidateObservedAmbiguous`. See [risks, command and stop procedure](HYPERX_NGENUITY_COEXISTENCE.md). Implementation is ready for review, not hardware-validated; do not run the physical experiment before owner review. No production providers change.
