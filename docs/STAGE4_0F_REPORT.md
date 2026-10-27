# Stage 4.0F implementation report

> Stage 4.0H adds optional, default-off automatic monitoring on the supervised helper. The manual-only/process-running restrictions below describe the earlier stage. See [current monitoring behavior](HYPERX_AUTOMATIC_MONITORING.md), including session consent, safety latches, backoff and outstanding Windows validation.

Branch: `feature/stage4-0f-hyperx-native-transport`, created from latest remote main `4dbf1a30f48063ab181a24c6202ba517809a7ad7` (merged integration PR #3). Earlier HyperX stages are already in main; this branch contains only Stage 4.0F. Commit/PR or comparison link is supplied in the delivery response. No merge, branch deletion or hardware request is authorized/performed here.

## Result and enablement

The experimental manual query path is implemented using a production-owned, self-contained helper. The real button is **conditionally functional**: default OFF; session-only Yes acknowledgement; exact receiver/capabilities; successful live no-HID containment/normal-exit/forced-exit preflight; no failure latch. Any missing/failed safety gate leaves it disabled. Opt-in/preflight never queries the receiver. Native execution requires a subsequent explicit Query click, then parent/helper NGENUITY checks. The current production path refuses closed/unknown NGENUITY state; the separate POC's closed-app guard remains intact.

One fixed 62-byte 66/89 output, one pre-armed input, a three-second native exchange deadline, no retries/polling. Candidate data always remains ownership-ambiguous with observation time/source. ConfirmedFresh is never emitted. Parent/helper re-enumeration can update absence/ambiguity if the receiver changes between scans; reconnection never schedules a query or revives a stale value.

## Lifetime decision

Use process isolation rather than loading native HID report I/O into the tray. Windows Job membership is assigned atomically during suspended CreateProcess using JOB_LIST/HANDLE_LIST; only pipe handles are inherited. Membership is verified before resume/arm. Unarmed creation failures are terminated using their retained exact process handle, never resumed uncontained. A global cooperating semaphore/owned process gate plus the legacy POC gate enforces single-flight. Delayed startup remains factory-owned and cancelled; it cannot receive an arm or be silently lost after a ready timeout.

The helper shares the audited POC native implementation in HyperXNative; native buffers/OVERLAPPED state never enter the parent. Candidate acceptance requires valid IPC/data and observed **normal process exit 0**, not only a returned task or cancellation request. Stop is cooperative first, then at most one explicit Job termination request and bounded exit observation. Forced/unconfirmed outcomes are failures, never accepted candidates. If exit is unconfirmed, registry ownership/global lease are retained, later Exited releases them, and querying latches off. Parent exit closes the non-inherited kill-on-close Job. Windows can retain kernel state for a defective driver; this is reported rather than called successful cleanup. No universal finite kernel-reclamation promise is made.

The [native transport ADR](HYPERX_NATIVE_TRANSPORT.md) documents SDK/OS contracts, callback/API-return/cancel leases, budgets, cancellation-race correction, IPC privacy and the exact pending-approval one-shot checklist. Readiness establishes the reviewed isolation mechanism and actual generic Job/process behavior; it does not prove real HID-driver cancellation, response ownership or repeated coexistence reliability.

## Implementation groups

- New projects: HyperXHelperProtocol (pure bounded IPC/fixed packet), HyperXNative (shared audited native transport) and HyperXHelper (production child). Existing POC native sources were moved, not copied; public namespaces remain compatible.
- Core: supervised transport, session readiness/failure state, bounded invocation versus native exchange timing, late-fault observation and helper-derived receiver changes. Native calls are not added to Core.
- Windows: atomic Job/pipe/process adapter, session acknowledgement, query/preflight/cancel controls, responsive owned tasks and graceful shutdown. Existing popup positioning/lifecycle and Logitech reader/monitor/icon are preserved.
- Build: F5 helper copy, explicit self-contained helper publish, static package gate integrated into the existing publisher. No new package/version dependencies; existing NuGet hashes unchanged.
- Tests/docs: 36 new portable cases, 5 new Windows cases, ADR/manual checklist/README/build documentation and PR description.

## Observed verification

| Check | Observed result |
| --- | --- |
| Locked solution restore | Passed |
| Release solution compilation | Passed, 0 warnings / 0 errors |
| Full portable Release suite | **265 passed, 0 failed, 0 skipped** (229 existing + 36 new) |
| Existing verification-runner contracts | **15 passed**, test.ps1/clipboard code unchanged |
| Windows-specific tests | **10 compiled, not run on Linux** (6 layout/control + 4 process/ABI tests) |
| Release publish/static component gate | Passed: helper/runtime present, parent native/POC dependency absent, helper independent of tray/SQLite/diagnostic executables |
| Source/lock/diff audit | G915 reader/monitor/icon, launcher/verification scripts and tray placement/lifecycle tail unchanged; existing NuGet versions/hashes unchanged; whitespace check passed |
| Physical HID / NGENUITY / real Windows UI | **Not run**; no hardware-validation claim |

The full run initially reproduced one existing timer/cancellation assertion failure; it was fixed without weakening/removing the assertion, sleeping or retrying. Focused cancellation/helper/POC tests passed, then the final full suite passed. Initial publish smoke found a removed child PublishDir property; the MSBuild target was corrected and the package gate passed afterward. Those intermediate failures are not counted as successful checks. Windows CI is configured to run the no-HID process/layout suite on push, but no remote outcome is claimed here.

The Windows process tests wrap the real factory with a **NativeOneShot prohibition**. They exercise normal/forced exit, unarmed termination, owner-close kill-on-close and ABI layouts using simulation modes only. Portable tests cover startup-off, acknowledgement, narrow schemas/frames, target/protocol validation, global single-flight, process uncertainty, error/failure cleanup, stale/disconnected states and fake provider end-to-end. The original exact-payload/capability/read/output/cancellation tests remain.

## Artifact and owner handoff

Release ZIP: `artifacts/PeripheralBatteryCompanion-stage4-0f-win-x64.zip` (ignored local build output). It includes the tray EXE, notices and the mandatory self-contained `hyperx-helper` directory. Final packaging is rebuilt after commit so helper build/revision identifies the delivered source. Do not copy only the main EXE. F5 copies the same component into Debug/Release build output; no diagnostics launcher is used by the app.

Review code/ADR and run the Windows no-HID suite before any physical test. A later **separately approved** attempt uses NGENUITY normally open, no other POC, one receiver, successful preflight and **one Query click**. Copy result/cleanup diagnostics and compare the displayed percentage/audio/NGENUITY state; then wait for review. Matching values establish consistency only. On timeout, interference, reset or unconfirmed cleanup, stop; no repeat/elevation/driver/service workaround. Prior 56% evidence authorized only the prior observation. Further capture/retry/polling remains outside this stage.
