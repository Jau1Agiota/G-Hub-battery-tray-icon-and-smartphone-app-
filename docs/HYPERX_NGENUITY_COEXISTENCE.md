# Controlled NGENUITY coexistence evaluation

## Status and execution gate

The owner verified the isolated POC's SET_REPORT and matching 62-byte interrupt response on hardware; see [wire evidence](HYPERX_USBPCAP_VERIFICATION.md). Coexistence has **not** been evaluated. This document prepares that evaluation; it does not change the executable or enable concurrent requests.

The existing active POC requires `ConfirmNgenuityClosed` and refuses detected NGENUITY processes. **Do not pass that acknowledgement while NGENUITY is running, rename processes, remove the guard, or use a direct executable invocation to bypass it.** A separately reviewed, explicit coexistence-only mode is required before the active concurrent test below is executable. Preserve the current default/closure guard, fixed payload, one-read/one-output limit, three-second exchange deadline, cancellation ownership and no-retry behavior. Such a mode is not implemented by this documentation update. No production provider or polling is authorized.

## Shared access and limits of correlation

The POC opens the selected COL01 interface with GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE and FILE_FLAG_OVERLAPPED, after zero-access capability inspection. Its output uses the already verified FILE_ANY_ACCESS output-report IOCTL. Sharing flags permit compatible access; they do not guarantee NGENUITY uses compatible sharing or that another process's existing handle allows this open. An access/sharing refusal is a valid outcome, not a reason to request exclusive access, elevate, stop services or broaden permissions.

Two applications may issue identical 66/89 requests. The receiver's response has no verified transaction identifier; bytes 2/3 remain unknown. A pre-armed read, matching header, correct percentage and nearby USB response do not prove that a report belongs to this process's request. Driver buffering/report delivery across handles must be observed rather than assumed to be broadcast or consumed by only one reader. The current pre-buffered-input refusal reduces one risk but cannot remove this ambiguity. USBPcap records device traffic, not originating process IDs. Successful reads by both clients alone do not establish reliable production coexistence.

## First step: metadata-only coexistence

1. Open NGENUITY normally and leave the headset page stable. Record the displayed percentage, audio state and tool/POC versions. Do not change settings, update firmware or repeatedly refresh the UI.
2. Start a short USBPcap capture on the receiver's current root hub. Identify current VID/PID, bus and address afresh. Record a brief baseline of NGENUITY's naturally occurring traffic; do not generate commands to provoke it.
3. Run the explicit isolated passive shared-access probe once:

   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\hyperx-battery-poc.ps1 -PassiveCoexistence -CopySummary
   ```

4. Stop capture, record whether metadata access succeeded, and confirm NGENUITY and ordinary audio still work. Share the sanitized summary and capture observations before proceeding. The probe sends and consumes no reports; any traffic observed is not proof it transmitted a request. It selects exactly one VID_03F0/PID_05B7/MI_03/COL01, opens zero-access metadata with read/write sharing, inspects and validates FF13:0001 and report lengths 62/62/0, then opens/closes GENERIC_READ with FILE_SHARE_READ | FILE_SHARE_WRITE, OPEN_EXISTING and FILE_FLAG_OVERLAPPED. GENERIC_READ is the minimum needed to test the active input handle's access compatibility, while metadata alone needs zero access. No ReadFile, DeviceIoControl, active session, queue drain or battery request is invoked. This tests shared read-handle opening, not response delivery/correlation.

## Later active trial: only after an explicit isolated mode is reviewed

Provide one command for that mode, await its output, and do not supply a sequence of repeat commands. No runnable active-coexistence command exists yet.

- Keep one receiver, NGENUITY and its helper running normally; no second POC instance. Record NGENUITY percentage and existing audio behavior. Start a short capture before invocation and include a baseline interval. Do not modify NGENUITY settings to suppress its traffic.
- The isolated mode must try only the same shared GENERIC_READ handle and validated COL01 capabilities. If open fails, report the numeric error/stage, dispose owned resources and stop without output, fallback or retry.
- If access succeeds, pre-arm one read and attempt at most one verified 62-byte 66/89 request, with the existing shared three-second exchange deadline. Do not flush/drain input queues, add initialization, transmit alternative commands, or poll. Pre-existing input should produce refusal, not be discarded until a preferred report appears.
- Stop capture after completion/error. Record shared-open outcome, request-attempted/output-completed flags, result, exit status, bounded response and time window. Compare NGENUITY and audio afterward without additional POC requests.
- Inspect SET_REPORT setup fields/body and submission/completion pairs, completed input data/status/endpoint and all other receiver requests in the window. NGENUITY may itself send identical battery requests or its own initialization. Do not attribute all traffic or a total request count to the POC. If multiple identical transactions overlap, label correlation **inconclusive**, even if both show the same percentage.
- If uncertainty remains, review the capture first and plan a separate bounded trial or additional process-attribution instrumentation. Do not invent a sequence field, alter the verified payload, or infer ownership from timing alone.

## Safe outcomes and stop rules

| Observation | Required handling |
| --- | --- |
| Sharing/access denied | Report unavailable for this trial; no exclusive/elevated fallback |
| Input already buffered, malformed or unexpected response | Refuse/reject; no queue drain or second read/request |
| Timeout, cancellation or output error | Delivery may have occurred; cancel owned I/O, retain buffers until safe completion, stop and review capture before any new attempt |
| Matching response but ambiguous request ownership | Record candidate only; mark freshness/correlation inconclusive |
| Audio disruption, NGENUITY error, device reset or unexpected POC-attributed output | Stop the trial; no repeat, service restart or driver change as a workaround |
| Successful shared access and matching value | Record this observation only; do not declare general coexistence validated |

Existing cleanup retains pinned buffers/OVERLAPPED storage until native completion and submission/cancellation leases end. A broken non-completing driver can defer reclamation to standalone process exit; this is not proof of safe long-running provider lifetime. Review process exit and available handle/resource observations after each approved trial without retaining raw machine identifiers in shared logs.

## Report and production gate

Share versions/commit, NGENUITY/helper state, metadata and active-open outcomes, numeric errors, POC result/exit, bounded capture frame references and bytes, background identical requests, correlation confidence, displayed percentages, audio continuity, and cleanup observations. Keep raw captures local; omit HID instance paths, USB serials and unrelated traffic.

Coexistence remains pending until shared active access, report delivery/correlation limitations, non-interference and failure cleanup have been reviewed on hardware. Any unresolved freshness or access problem blocks treating a concurrent candidate as a fresh production reading. After evaluation, separately plan conservative polling, no overlapping exchanges, stale/unavailable semantics, disconnect/reconnect handling and bounded resource cleanup. Do not integrate production code as part of this procedure.

## Passive probe reporting and verification

The report contains a fixed sanitized COL01 identity, capabilities/counts, discovery/inspection status, whether shared open was attempted, desired access/sharing/overlapped flags and numeric Win32 or HID errors by stage. It omits raw paths/instance IDs. A successful shared handle is immediately disposed; metadata/preparsed/enumeration resources retain existing SafeHandle/finally ownership. There is no fallback, permission escalation or retry. Missing receiver exits 3, ambiguity 5, metadata/access failure 1, unsupported platform/invalid combinations 2, success 0. Clipboard/build handling remains the existing launcher's behavior.

`-PassiveCoexistence` cannot be combined with preview, read or closure-confirmation switches. NGENUITY is permitted only for this non-report mode; active flags and process guards remain unchanged. Preview still opens no handle and is not a coexistence result. Access denial/sharing violation is a test outcome to report, not a reason to rerun elevated.

Implementation verification: Release focused tests passed, 46 total (37 existing POC cases and 9 new passive cases), zero build warnings/errors. New tests cover exact target/deduplication, missing/ambiguous/incomplete enumeration, metadata/HID errors, capability mismatch, numeric access/sharing denial without retry, sanitized formatting and mode isolation. Synthetic tests do not exercise Windows CreateFile/CloseHandle or physical NGENUITY coexistence. Full local suite and Windows results remain owner verification.
