# Stage 4.0A delivery report

Branch: `feature/stage4-0a-passive-g305-inspector`, based on Stage 3.1.1 commit `a9ee96f`. Final commit is supplied in the delivery response / Git history. No merge or production G305 battery provider is included.

## Implementation

An independent, dependency-free .NET 10 console project uses Windows SetupAPI/HID P/Invoke to enumerate present HID interface paths, filter exact `046D:C53F / MI_02 / COL01-COL03` identities, and obtain preparsed data/capabilities through handles opened with desired access zero. It excludes the G915 `046D:C547` receiver and unrelated interfaces before opening device handles.

The report includes actual usage page/ID, input/output/feature report byte lengths, button/value/data-index counts and link collection node count. It never substitutes expected usages for actual values. Interface instance identifiers do not enter the report model and are omitted from terminal/clipboard output. Multiple matching receivers are listed separately without inferring physical-parent grouping.

The import surface is limited to nine metadata/open/cleanup APIs. There is no HID feature request, report transmission, raw input read, broader-access fallback, device configuration, G HUB integration, database access or polling. SafeHandle/using scopes own interface/preparsed/device-info handles, and a finally block frees the bounded SetupAPI detail buffer. P/Invoke layout/status/BOOLEAN conventions are documented and tested.

Root `diagnose-g305.ps1` performs a locked restore/build of only this project, runs it once, preserves command error codes, and optionally copies the sanitized report via Set-Clipboard. It supports ordinary Windows PowerShell 5.1/PowerShell 7 syntax and documents a process-only ExecutionPolicy Bypass invocation. No settings changes, elevation, separate installer, persistent logs, or additional package dependencies are needed.

## Regression baseline and checks observed

The previous task recorded Release compilation and 11 focused Stage 3.1.1 regressions passing on Linux. The most recent owner-provided full Windows baseline before those fixes was 62 passed / 1 failed in Stage 3.1. No later full Windows run or hardware result is inferred from the request to implement Stage 4.0A. The explicit passive-inspector request authorizes this stage without claiming earlier Windows validation.

Current checks:

- Release solution build: **passed**, zero warnings/errors. All solution projects, including the independent inspector and existing Windows layout tests, compiled.
- Locked solution restore: **passed**. No new NuGet package/version was added; the existing core test lockfile adds only the diagnostic project reference, and the standalone project's lockfile has an empty package dependency graph.
- New `HidInspectorTests` only: **33 passed, 0 failed, 0 skipped** on Linux. Covers filtering, G915/other-device exclusion, malformed/substring identities, capability formatting/zero lengths, expected-usage comparisons, multiple receivers/duplicates, partial and numeric errors, identifier redaction, structure sizes and metadata-only P/Invoke whitelist. No HID native call is made by these synthetic tests.
- PowerShell syntax check: **passed** using PowerShell 7.5.4 on Linux.
- Actual script and executable non-Windows guards: **passed**, exit 2, no native inspection/clipboard call.
- Three temporary wrapper smoke cases (success, diagnostic failure, build failure): **passed** with fake commands and a mocked clipboard. Only a temporary script copy bypassed its platform guard for these control-flow tests; production source was unchanged. These are not Windows, hardware or real clipboard verification.
- Existing production battery/monitoring/tray code, existing regression test files and `test.ps1`: unchanged from the branch base.

The full regression suite was not rerun, in accordance with the owner's preferred local workflow. Real SetupAPI/HID access, native handle lifecycle under the receiver driver, Windows PowerShell 5.1, the Windows clipboard, receiver capability values and remote Actions results remain unverified here.

## Owner handoff

```powershell
.\test.ps1 -Release -CopySummary
.\diagnose-g305.ps1 -CopySummary
```

If policy blocks the diagnostic script:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\diagnose-g305.ps1 -CopySummary
```

Share the full-test summary and the sanitized capability report. See [G305_HID_INSPECTOR.md](G305_HID_INSPECTOR.md) for scope, numeric errors, expected/missing collections, no-match behavior, multiple-receiver handling, privacy and exact local checks. A zero-access failure is a valid diagnostic outcome; do not use elevation or active HID commands as a workaround.

The framework-dependent build is `src/PeripheralBatteryCompanion.G305Inspector/bin/Release/net10.0/PeripheralBatteryCompanion.G305Inspector.dll`. The entry point builds the proper local host on Windows and invokes this DLL through dotnet. It is separate from the tray executable/ZIP; no new packaged release is claimed. G305 battery protocol/condition, connectivity and production battery integration remain unknown/unimplemented.

## Files changed

- `AGENTS.md`
- `PeripheralBatteryCompanion.sln`
- `README.md`
- `diagnose-g305.ps1`
- `docs/G305_HID_INSPECTOR.md`
- `docs/RESEARCH.md`
- `docs/STAGE4_0A_REPORT.md`
- `docs/TESTING.md`
- `src/PeripheralBatteryCompanion.G305Inspector/CapabilityInspector.cs`
- `src/PeripheralBatteryCompanion.G305Inspector/InspectionReport.cs`
- `src/PeripheralBatteryCompanion.G305Inspector/NativeMethods.cs`
- `src/PeripheralBatteryCompanion.G305Inspector/PeripheralBatteryCompanion.G305Inspector.csproj`
- `src/PeripheralBatteryCompanion.G305Inspector/Program.cs`
- `src/PeripheralBatteryCompanion.G305Inspector/ReceiverFilter.cs`
- `src/PeripheralBatteryCompanion.G305Inspector/ReportFormatter.cs`
- `src/PeripheralBatteryCompanion.G305Inspector/WindowsHidMetadataBackend.cs`
- `src/PeripheralBatteryCompanion.G305Inspector/packages.lock.json`
- `tests/PeripheralBatteryCompanion.Tests/HidInspectorTests.cs`
- `tests/PeripheralBatteryCompanion.Tests/PeripheralBatteryCompanion.Tests.csproj`
- `tests/PeripheralBatteryCompanion.Tests/packages.lock.json`
