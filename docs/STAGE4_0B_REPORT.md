# Stage 4.0B implementation report

Branch: `feature/stage4-0b-passive-hyperx-inspector`.

Base: latest merged main fetched at the start of this task, `2b64edc34c59ee8925acb325f5be7fb1d0cafbc9` (Stage 4.0A merge). The branch was created directly from origin/main with a clean working tree. Final commit is in the delivery response / Git history. No merge is performed by this task.

## Implementation

`diagnose-hyperx.ps1 -CopySummary` adds a Windows PowerShell 5.1-compatible entry point. It uses the existing console project with a closed `--hyperx` selector. The original `diagnose-g305.ps1` is byte-for-byte unchanged, and no arguments still select G305.

One immutable profile class supplies fixed identifiers and expected usages to the existing filter, inspector, formatter and zero-access native backend. The project/namespace is intentionally retained to avoid unnecessary renaming, dependencies or deployment changes. G305's filtering, scope, report and inspection order remain its defaults. HyperX uses `03F0:05B7 / MI_03`, inspects COL01 first, and reports all three expected collections: COL01 `FF13:0001`, COL02 `000C:0001`, COL03 `000B:0005`. Optional hardware-ID revision tokens do not exclude a receiver; supplied revision 4109 is explicitly labeled reference-only, not queried firmware.

Reports retain actual capability values, mark expectation differences, separate missing collections from access/inspection errors, preserve partial results and numeric exit/status codes, and omit opaque instance identifiers. Multiple receivers remain separate. The native interop module is unchanged: metadata-only imports, desired access zero, shared read/write opens, existing SafeHandle/unmanaged-buffer cleanup, and no access fallback. No input sampling, feature request, output report, audio setting/driver change, NGENUITY control, battery decoding or production provider is introduced.

## Verification observed

| Check | Observed result |
| --- | --- |
| Locked solution restore | Passed, no package/lockfile changes |
| Release solution compilation | Passed, zero warnings/errors, including existing Windows Forms test assembly |
| Entire hardware-independent Release suite | **139 passed, 0 failed, 0 skipped** |
| Existing cases within that suite | **106 passed**, including all 33 original G305 cases |
| New HyperX synthetic cases | **33 passed** |
| Existing PowerShell test-runner contract checks | **15 passed** with fake commands/clipboard |
| Both diagnostic launchers' syntax checks | Passed using PowerShell 7.5.4 on Linux |
| Both launchers' simulated control flow | **8 cases passed**: success, diagnostic failure, build failure, clipboard failure for each launcher; HyperX flag forwarding and unchanged G305 invocation verified |
| Actual CLI/launcher non-Windows guards | Passed, exit 2; unsupported report/capture arguments also rejected before inspection |
| Three existing Windows Forms tests | Compiled, **not run** on Linux |
| Actual Windows HID/PowerShell 5.1/clipboard/audio/NGENUITY/hardware behavior | **Not run** here |
| Remote GitHub Actions | No result observed/claimed |

The full portable suite ran once after the implementation, with no assertions weakened or tests removed. TRX counts were checked from the current run: 106 existing and 33 new cases all passed. New cases cover exact VID/PID/MI/COL/revision handling, malformed/lookalike identifiers, priority order, three usage pairs and numeric capability fields, missing/error/partial results, redaction, duplicate/multiple receivers, actual-versus-expected usage, invalid results, closed CLI selection and G305 default/profile isolation. They use synthetic paths/data, not physical device results.

Temporary wrapper fixtures only overrode the platform guard in copies outside the checkout, and used fake dotnet and clipboard operations. Production launchers retain their actual Windows guard; no device was accessed by those checks.

Production battery models/readers/monitoring/tray code, all pre-existing test files, `test.ps1`, its clipboard logic, NativeMethods and existing dependencies are unchanged. Windows CI already uses the same full local runner and discovers the new tests automatically; its actual run must still be observed. Because the three Windows-only tests could not execute here, the all-tests-on-Windows acceptance criterion remains pending. Physical success is not claimed.

## Owner validation

Run on Windows, sharing each copied result before the next clipboard command replaces it:

```powershell
.\test.ps1 -Release -CopySummary
.\diagnose-g305.ps1 -CopySummary
.\diagnose-hyperx.ps1 -CopySummary
```

If execution policy blocks the new launcher:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\diagnose-hyperx.ps1 -CopySummary
```

See [HYPERX_HID_INSPECTOR.md](HYPERX_HID_INSPECTOR.md) for the hardware identifiers, comparison procedure, zero-access errors, sanitized path behavior and current limits. Check actual usages and report lengths/counts across all observed collections, G305 isolation, instance-ID omission, normal completion/reopening and ordinary audio/NGENUITY behavior. A nonzero report length is not proof of battery telemetry. COL01 remains a candidate until actual findings justify further research.

Stage 4.0C input capture is not implemented. It requires actual Windows hardware results and separate review of bounded, non-invasive, opt-in capture; decoding needs an independent reference before production integration.

## Files changed

- `AGENTS.md`
- `README.md`
- `diagnose-hyperx.ps1`
- `docs/G305_HID_INSPECTOR.md`
- `docs/HYPERX_HID_INSPECTOR.md`
- `docs/RESEARCH.md`
- `docs/STAGE4_0B_REPORT.md`
- `docs/TESTING.md`
- `src/PeripheralBatteryCompanion.G305Inspector/CapabilityInspector.cs`
- `src/PeripheralBatteryCompanion.G305Inspector/HidInspectionTarget.cs`
- `src/PeripheralBatteryCompanion.G305Inspector/InspectionReport.cs`
- `src/PeripheralBatteryCompanion.G305Inspector/Program.cs`
- `src/PeripheralBatteryCompanion.G305Inspector/ReceiverFilter.cs`
- `src/PeripheralBatteryCompanion.G305Inspector/ReportFormatter.cs`
- `src/PeripheralBatteryCompanion.G305Inspector/WindowsHidMetadataBackend.cs`
- `tests/PeripheralBatteryCompanion.Tests/HyperXInspectorTests.cs`
