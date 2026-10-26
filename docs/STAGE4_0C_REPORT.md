# Stage 4.0C implementation report: ready for payload review, not hardware-validated

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

## Review handoff: only the preview command now

Once the branch is checked out, the next owner command is hardware-free:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\hyperx-battery-poc.ps1 -PreviewRequest -CopySummary
```

Paste the preview and confirm the entire 60-zero tail against the original capture. **Wait for review before a hardware request.** Do not infer permission to retry from an error or timeout. Actual physical commands will be supplied one at a time, awaiting output.

The [technical guide and physical-device checklist](HYPERX_BATTERY_POC.md) explain exact bytes, SET_REPORT setup/data verification, NGENUITY/helper closure, repeated-percentage comparisons, unplug/off/absent/timeout tests and audio checks. Initial wire validation must confirm 21/09, wValue 0266, wIndex 3, wLength 62 and the complete body. Address 20, root hub assignment and endpoint 84 are supplied observations, not hardcoded target selectors.

After actual request/capture agreement, compare the candidate against NGENUITY reopened **after** stopping that capture. Only later successful physical validation can justify extracting a provider, polling, NGENUITY coexistence or tray integration. None is included here.

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
