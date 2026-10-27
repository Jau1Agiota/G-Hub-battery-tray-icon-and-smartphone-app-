# Consolidated HyperX integration: stages 4.0B–4.0E

## Shipping behavior and safety gate

The tray now includes HyperX Cloud III Wireless as a separate device, without changing the G915 database reader, its 30-second monitor, G305 placeholder, tray icon meaning or popup lifecycle. Startup and explicit Refresh inspect HyperX **metadata only**: exact VID 03F0 / PID 05B7 / MI_03 / COL01, FF13:0001, input/output 62/62, feature 0. Missing, multiple or incompatible interfaces do not select a fallback. Receiver presence does not prove the wireless headset is connected; connectivity always remains Unknown.

There is **no native active transport wired into the tray**. This deliberately follows the Stage 4.0E briefing's fallback: if in-process lifetime safety cannot be demonstrated, disable production active querying and report the blocker. The existing POC owns pending OVERLAPPED storage until native completion and can rely on standalone process exit for a defective driver. That is not sufficient proof for an indefinitely running tray. Copying its unsafe I/O into the provider or merely trusting a three-second task timeout would not resolve that risk.

The experimental preference defaults off, is memory/session-only, and requires a Yes acknowledgement of NGENUITY response competition/disruption. Opting in does **not** override the lifetime gate: the query button stays disabled and the reason remains visible. Restart resets the preference off. There is no startup query, timer, automatic HyperX battery polling, queued query, settings/service/driver change, or invocation of the POC from the tray. Neither closed-NGENUITY nor concurrent active native querying is enabled in this build. Existing standalone POC modes retain their separate safeguards.

The manual-query controller is implemented/tested behind a transport boundary for future completion-safe work, using fakes only. It requires explicit opt-in, a verified-lifetime transport, fresh unambiguous receiver metadata, observed NGENUITY, and one non-queued in-flight operation. Its deadline is three seconds for the exchange, with no retry. A failure stops further attempts for that provider session; toggling the preference does not clear that stop. No real transport currently satisfies this gate. **This is a production metadata/state/UI integration, not a usable active production battery reader.**

## Evidence and limits

| Stage | Included result | Boundary |
| --- | --- | --- |
| 4.0B | Passive three-collection HyperX inspector, sanitized output, launcher, synthetic tests | Metadata is not battery telemetry |
| 4.0C | Fixed 62-byte 66 89 + 60-zero request; successful input ending in byte-4 candidate 56%, matching NGENUITY; exact on-wire SET_REPORT verified | No verified transaction identifier or headset link-state field |
| Passive coexistence | Owner reports shared GENERIC_READ / READ\|WRITE / OVERLAPPED open succeeded with NGENUITY open, no report I/O | Sharing access alone does not establish response delivery |
| 4.0D | Owner reports one acknowledged concurrent experiment on 8ca502e: output completed, 56% candidate, exit 0, 77.5 ms; NGENUITY/audio nominal afterward | CandidateObservedAmbiguous; one observation does not establish reliable continuous coexistence |
| 4.0E | Production metadata discovery, separate receiver/battery states, opt-in preference, hard active-lifetime gate, sanitized diagnostics and fake-tested manual orchestration | Production active transport intentionally disabled pending completion-safe lifetime design and further approved validation |

The Stage 4.0E briefing supplied the 4.0D hardware result; Codex did not execute it or independently inspect another capture. Earlier Stage 4.0D implementation-time reports describe the state before that result. Successful wire setup remains 21/09, wValue 0266, wIndex 3, wLength 62; bytes 2/3 remain uninterpreted. The zero-length IN URB following the captured response is not another battery sample. Percent agreement establishes consistency, not unique ownership or freshness under concurrent NGENUITY polling. Do not infer charging, device connectivity or new protocol commands.

## Architecture and state semantics

- `HidMetadata` is the shared class library for existing SetupAPI/HID metadata code. Its namespace remains compatible with the inspector. The inspector and POC entry points remain separate. The extraction is necessary because a self-contained Windows app cannot reference the framework-dependent inspector executable (NETSDK1150). No second native implementation, new native import or package dependency was added.
- `HyperXCollectionRules` centralizes exact target/capability selection for the POC and production metadata adapter. Enumeration/preparsed/file handles retain their existing SafeHandle/finally ownership. Metadata access requests zero access with read/write sharing.
- `HyperXBatteryProvider` implements the new per-device `IDeviceBatteryProvider` using the existing `BatteryDevice` model. The original keyboard-shaped `IBatteryProvider`/`BatteryReadResult` remain unchanged; no HyperX device is mislabeled as a keyboard or database read.
- `BatteryDevice` gains optional observation, receiver and source fields, preserving existing callers. Observation states are Unknown, Unavailable, Stale, CandidateAmbiguous and reserved ConfirmedFresh. The HyperX provider never emits ConfirmedFresh. Unknown has no observation; failures without history are Unavailable; retained history becomes explicitly Stale. A future validated reply may become CandidateAmbiguous with a UTC observation timestamp, not a device-provided timestamp. Disconnect/reconnect never refreshes an old candidate's time or makes it fresh. The shipped provider has no persisted/imported percentage and never initializes to 56%.
- `HyperXReceiverDiscovery` runs passive metadata work off the UI thread. It runs once at startup and on explicit Refresh, independently of G915 monitoring. The tray awaits discovery/provider shutdown before destroying its dispatcher. As with existing metadata APIs, task cancellation cannot forcibly interrupt a synchronous driver metadata call; no new pending active I/O is introduced.
- `IHyperXManualQueryTransport` is a contract for future lifetime-reviewed code, not an implemented native provider. Its typed results require validated response data; the existing POC remains the protocol/native reference. Fake transports test the controller. Completion uncertainty blocks promotion and stops attempts. Late transport faults are observed; cancellation never promises physical non-delivery. Future adapters must own buffers/handles through native completion and implement safe disposal; no production flag can claim that proof today.

Diagnostics are copied only on an explicit UI click. They contain fixed identity/state/source, receiver/link distinction, observation time, operation stage, numeric Win32/HID codes, request/output flags, elapsed time, ambiguity and completion-safe cleanup status. The last exchange diagnostic is retained independently of later discovery. Raw paths, instance IDs, serials, exception messages, arbitrary reports and USB capture files are not logged.

## Manual owner checklist (no battery request)

Review/build/test first. The following is a UI/metadata checklist, **not authorization for a physical active query**:

1. Start the Debug build without elevation, with NGENUITY either closed or normally open. Verify HyperX appears separately from G915/G305; battery is Unknown or Unavailable, not 56%. Note receiver detection and Unknown headset connectivity.
2. Confirm G915 still reads the existing database normally, tray icon/tooltip remain G915, and tray toggle/menu/dismissal/exit behavior is preserved. No database/WAL manipulation is needed.
3. Open the experimental preference, reject the warning, then accept it only if desired for this non-query UI check. Verify the query button stays disabled and the native-lifetime blocker remains visible. Restart and confirm preference is off again.
4. Use Refresh for metadata only. With no matching receiver, or after a separately agreed reconnect/absence check, confirm absence/inspection states never become a connected-headset claim. Multiple receivers fail closed. These hardware-state manipulations are optional separate owner actions, not automated implementation tests.
5. Copy sanitized HyperX diagnostics and confirm no raw HID paths/identifiers. Check layout/actions at 100/125/150/200% DPI, multiple monitors, screen edges and limited working areas. Exit while metadata discovery is pending and confirm clean shutdown.
6. Optional short USBPcap observation can check zero POC-originated battery queries on startup/Refresh/opt-in. NGENUITY's own traffic must not be attributed to the tray. Capture only under the owner's procedure; no capture was made by Codex.

Before any future active production test: separately review native lifetime/cancellation proof, failure-stop behavior and ownership ambiguity; implement/review an adapter; obtain explicit approval for **each** physical attempt. Do not enable retries/polling to collect evidence. A production reliability design must separately cover stale/unknown links, access denial, re-enumeration, cancellation races, NGENUITY coexistence, audio continuity and cleanup after timeout. Integration/squash merge does not itself authorize those tests.

## Build and debugging

Use Visual Studio 2026 and the existing Debug launch profile as documented in [BUILD.md](BUILD.md). `test.ps1 -Release -CopySummary` retains its existing clipboard behavior. The consolidated cloud verification is recorded in [HYPERX_FINAL_INTEGRATION_REPORT.md](HYPERX_FINAL_INTEGRATION_REPORT.md); Linux build/test success is not Windows UI or hardware validation. All automated USB interactions use fakes. Existing POC launchers remain explicit diagnostic entry points and are not invoked by build/test or production startup.
