# Stage 4.0C implementation report: physical comparison and wire transport verified; coexistence pending

Branch: `feature/stage4-0c-hyperx-battery-poc`, based on `d7a496b` (the existing Stage 4.0B branch; origin/main still contained Stage 4.0A when this task started). Final commit is provided in the delivery response / Git history. No default-branch merge is performed.

## Source review and proposed repository change

The current Windows temporary Program.cs and HyperXBatteryPOC.csproj were supplied in the conversation and reviewed before implementation. They are a dependency-free metadata-only .NET 10 inspector using zero-access shared handles, explicit Unicode interface detail lookup and path offset 4. The temporary project, its Program.Inspector.Backup.txt and earlier lab remain untouched. No assumption was made that they were already integrated.

A new isolated `PeripheralBatteryCompanion.HyperXBatteryPoc` project reuses the existing inspector's enumeration, target filter and metadata interop. Default behavior is passive HyperX inspection. Active mode exists only in this new project, requiring both a read flag and explicit NGENUITY/helper-closure acknowledgement; process-name checking is an additional conservative check, not proof of all helpers being closed. A named semaphore prevents concurrent active runs in the Windows session and supports release across async continuations.

The only proposed output is **62 bytes: 66 89 + 60 zero bytes**, including report ID. The handoff's packet was abbreviated, so the full zero tail must be confirmed against the original capture before transmission. No captured initialization commands, feature requests, raw command flags, repeat attempts or polling exist. Active selection permits only one unique `03F0:05B7 / MI_03 / COL01`, with `FF13:0001`, input/output lengths 62 and feature length 0, checked before and after opening the shared READ/overlapped handle.

The transport requests IOCTL_HID_SET_OUTPUT_REPORT 0x000B0195 via overlapped DeviceIoControl rather than assuming WriteFile uses the captured control transfer. One read is armed before the output; one response is validated for exact length, report ID, command and percentage range. Only byte 4 becomes a candidate; bytes 2/3 remain unknown. The three-second exchange deadline covers output plus input. Cancellation requests CancelIoEx without unbounded waits; pinned buffers/OVERLAPPED storage stay owned until native completion, API return and cancellation-call leases end. There is no freeing of pending buffers. Driver-level cancellation and actual USB transfer mapping remain hardware checks; deferred cleanup may rely on standalone process teardown for a non-completing driver. This is not a production-ready provider/lifetime policy for indefinite monitoring.

`hyperx-battery-poc.ps1` adds passive/default, payload preview and explicitly confirmed read modes, plus sanitized optional clipboard summaries. Preview is hardware-free. The original passive scripts, their executable behavior, existing interop whitelist, `test.ps1`, G915/G305/production monitoring and tray lifecycle are unchanged. The inspector project adds only an internal-access grant for native metadata reuse; the solution/test project reference the new isolated executable. No new package/version dependency is introduced. Real USB capture files are gitignored.

## Evidence and observed verification

Supplied hardware evidence: 62-byte output prefix 66/89, responses 66 89 0F 1B 40 00 at NGENUITY 64% and 66 89 0F 27 42 00 at 66%. This supports a candidate byte-4 percentage, not a complete protocol, off-state/freshness guarantee or independently executed POC request. The user reports the temporary metadata inspector working on the actual PC; that is distinguished from new active transport validation.

- Release solution build: **passed**, 0 warnings/errors; isolated POC final build also passed after static-review corrections and final preview wording.
- Frozen restore: **passed**. Only a new project-reference entry/lockfile is introduced; existing NuGet versions/hashes remain unchanged.
- Entire hardware-independent Release suite: **176 passed, 0 failed, 0 skipped** (139 existing plus 37 new POC cases).
- A final-review regression was first reproduced: unexpected-error output incorrectly claimed no request had been attempted. A failing test was added, then fixed so unknown failures explicitly preserve uncertain delivery. Managed setup failures now report fixed type/HResult rather than exposing raw exception paths.
- New tests cover supplied 64/66 frames, boundaries, unknown fields, exact payload and mutation isolation, all lengths/headers/other-command rejections, COL01 selection/ambiguity, capability gate, default/opt-in parsing, one read/send, pre-buffered input refusal, write/read errors, timeout during output/input, cancellation, cleanup and native import/IOCTL constants. They use simulated sessions only.
- Existing three Windows Forms tests compiled, **not run** on Linux. The new framework NativeOverlapped size assertion is Windows-guarded and was not executed here. Native IOCP/driver cancellation, named semaphore behavior, actual Windows clipboard/PowerShell 5.1 and hardware operations remain unverified.
- PowerShell parser: **passed**. Actual CLI and launcher preview: **passed**, payload checked byte-for-byte as 62 bytes without HID access. Invalid flag combinations and Linux/platform guards: **passed**, no device I/O. Final preview explicitly flags the zero-tail assumption.
- The unsafe native I/O implementation was statically reviewed against SDK signatures, buffer ownership, callback/API-return races and cancellation. This is not proof of real driver behavior. No active invocation was executed on hardware, no full outgoing USB packet equivalence was claimed, and remote CI has not been observed.

## Owner validation update (2026-10-27)

The owner inspected the original `hyperx-battery-66.pcap` with TShark. Frame 9 confirmed SET_REPORT 21/09, wValue 0266, wIndex 0003, wLength 62, and the complete body `66 89` plus exactly 60 zero bytes. Other captured initialization commands remain excluded.

The owner then executed the isolated POC on the physical device. It returned `CandidateReceived`, 56%, with response prefix `66 89 0E E4 38` followed by zeros. NGENUITY opened afterward also displayed 56%. This establishes a successful independent comparison for that observation. It does not establish the POC's exact USB transfer, response freshness in other states, charging semantics, long-running cleanup or disconnection behavior. Bytes 2/3 remain uninterpreted. The cloud environment did not perform this hardware test or inspect a newly supplied capture.

The build/test results above describe implementation-time verification; no new build or test run is needed for this documentation-only update. Earlier pending-hardware statements above describe the implementation handoff, superseded only by the specific owner results recorded here.

Next: the [controlled USBPcap procedure](HYPERX_USBPCAP_VERIFICATION.md), one request and result review at a time. POC executable, launchers and production behavior remain unchanged. No production integration or polling is authorized by this update. After wire verification, plan conservative polling, unavailable/disconnected semantics, cancellation and resource-lifetime validation separately.

## Files changed

- `.gitignore`
- `AGENTS.md`
- `PeripheralBatteryCompanion.sln`
- `README.md`
- `docs/HYPERX_BATTERY_POC.md`
- `docs/RESEARCH.md`
- `docs/STAGE4_0C_REPORT.md`
- `docs/TESTING.md`
- `hyperx-battery-poc.ps1`
- `src/PeripheralBatteryCompanion.G305Inspector/PeripheralBatteryCompanion.G305Inspector.csproj`
- `src/PeripheralBatteryCompanion.HyperXBatteryPoc/ActiveNativeMethods.cs`
- `src/PeripheralBatteryCompanion.HyperXBatteryPoc/BatteryAttempt.cs`
- `src/PeripheralBatteryCompanion.HyperXBatteryPoc/HyperXStatusProtocol.cs`
- `src/PeripheralBatteryCompanion.HyperXBatteryPoc/PeripheralBatteryCompanion.HyperXBatteryPoc.csproj`
- `src/PeripheralBatteryCompanion.HyperXBatteryPoc/PocFormatter.cs`
- `src/PeripheralBatteryCompanion.HyperXBatteryPoc/PocPlan.cs`
- `src/PeripheralBatteryCompanion.HyperXBatteryPoc/Program.cs`
- `src/PeripheralBatteryCompanion.HyperXBatteryPoc/WindowsBatterySession.cs`
- `src/PeripheralBatteryCompanion.HyperXBatteryPoc/WindowsBatterySessionFactory.cs`
- `src/PeripheralBatteryCompanion.HyperXBatteryPoc/packages.lock.json`
- `tests/PeripheralBatteryCompanion.Tests/HyperXBatteryPocTests.cs`
- `tests/PeripheralBatteryCompanion.Tests/PeripheralBatteryCompanion.Tests.csproj`
- `tests/PeripheralBatteryCompanion.Tests/packages.lock.json`

## Owner update: POC wire transport verified (2026-10-27)

The owner reports `Hyperx.pcapng` frame 10061: successful control OUT SET_REPORT 21/09, wValue 0266, wIndex 3, wLength 62, exact body 66 89 + 60 zeros. Frame 10062: successful interrupt IN endpoint 84, 62 bytes beginning 66 89 0E E3 38, matching the POC response and 56% candidate. Frame 10063 is a successful zero-length IN URB, not another battery reading. The earlier NGENUITY comparison also showed 56%.

This supersedes earlier pending-wire statements for this observed hardware exchange; it does not validate concurrency, disconnection or long-running resource lifetime. See the [wire record](HYPERX_USBPCAP_VERIFICATION.md) and [coexistence test plan](HYPERX_NGENUITY_COEXISTENCE.md). Coexistence is the next gate. The existing active mode still requires NGENUITY closed; do not bypass that guard to test concurrency. No executable, launcher or production behavior changes accompany this documentation update.
