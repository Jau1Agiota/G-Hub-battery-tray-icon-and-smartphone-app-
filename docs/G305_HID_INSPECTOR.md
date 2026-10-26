# Stage 4.0A: passive G305 receiver capability inspector

This is a separate, one-shot Windows diagnostic, not a battery provider. It uses original implementation code and standard Microsoft SetupAPI/HID APIs. It does not use G HUB, its SQLite database, third-party G HUB integrations, or Logitech protocol commands. It has no NuGet/runtime package dependency beyond .NET 10 and Windows system libraries.

## Run and share

From the checkout on Windows 11, using an ordinary user PowerShell terminal with the SDK from `global.json` available:

```powershell
.\diagnose-g305.ps1 -CopySummary
```

Without `-CopySummary`, the same sanitized report is printed only to the terminal. The script resolves paths relative to itself, so it can also be called with `& 'C:\source folder\PeripheralBatteryCompanion\diagnose-g305.ps1' -CopySummary` from another directory. It performs a locked restore/build of just the independent inspector, then runs it once. It does not start the tray app or require administrator privileges.

If script execution is blocked, use the same per-process execution-policy workaround used for repository scripts:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\diagnose-g305.ps1 -CopySummary
```

For PowerShell 7, replace `powershell.exe` with `pwsh.exe`. This selects policy for that process only; the utility does not call Set-ExecutionPolicy or modify machine/user settings. Windows PowerShell 5.1 and PowerShell 7 syntax are supported; actual Windows execution and clipboard behavior must be verified locally.

Paste the copied report, including errors or missing collections. Both terminal and clipboard deliberately omit the opaque interface instance segment, which can contain persistent device identifiers. The real interface path is obtained locally from SetupAPI and passed only to CreateFile; it is not saved, logged or copied. There is no raw-path/report-dump switch. Compiler/SDK failure summaries also redact checkout/profile path prefixes and obvious credentials in URLs. No diagnostic log files are created; only ordinary gitignored build outputs are produced.

The standalone framework-dependent build is in `src/PeripheralBatteryCompanion.G305Inspector/bin/Release/net10.0/`. It is separate from the tray application's published ZIP. Direct execution is possible with `dotnet <path-to-PeripheralBatteryCompanion.G305Inspector.dll>` on Windows; the PowerShell wrapper is the convenient build/run/clipboard entry point. No command arguments or active-report mode are accepted by the executable.

## Exact scope and report

The candidate hardware supplied by the owner is vendor **046D**, product **C53F**, USB interface **MI_02**, with collections:

| Collection | Supplied expected usage page:ID |
| --- | --- |
| COL01 | FF00:0001 |
| COL02 | FF00:0002 |
| COL03 | FF00:0004 |

Filtering uses exact, case-insensitive hardware-ID tokens from HID interface paths, including the collection identifier. Other vendors, other products (especially the G915 receiver **046D:C547**), MI_00/MI_01, unknown collections and substring lookalikes are excluded before any device handle is opened. SetupAPI necessarily enumerates HID interface paths to apply that filter; it does not open unrelated devices or sample their input reports.

For each match, the report shows:

- A sanitized path template identifying VID/PID/MI/COL, with the actual instance segment omitted.
- Actual usage page and usage ID from HIDP_CAPS, in hexadecimal.
- Input, output and feature report byte lengths, including the report-ID byte where applicable, exactly as reported by Windows.
- Input/output/feature button-capability, value-capability and data-index counts, plus link collection node count. These are descriptor/capability counts, not decoded reports, physical button counts, or battery fields.
- A fixed operation name and Win32 error or HID NTSTATUS if metadata inspection failed.

A zero report length or capability count is valid metadata, not a fabricated missing value. Unexpected usage values are shown unchanged with a note against the supplied expectation. A nonzero feature/output length describes a capability; it does not prove a battery protocol, authorize a request, or imply that reading battery data is supported. Missing expected collections are marked **not observed**. Multiple matching receivers remain separate numbered entries rather than being merged by collection number; no physical parent grouping is inferred.

The owner reported all three collections under one USB parent interface. This diagnostic does not independently verify that relationship and does not claim C53F uniquely identifies a particular physical mouse. It reports candidate receiver metadata only. No battery level, G305 qualitative condition or connectivity claim is produced.

## API and access contract

The complete application P/Invoke list is:

- `HidD_GetHidGuid`
- `SetupDiGetClassDevsW`, `SetupDiEnumDeviceInterfaces`, `SetupDiGetDeviceInterfaceDetailW`, `SetupDiDestroyDeviceInfoList`
- `CreateFileW`
- `HidD_GetPreparsedData`, `HidP_GetCaps`, `HidD_FreePreparsedData`

CreateFile is always **desired access 0**, share read/write, OPEN_EXISTING, flags 0. If that cannot obtain metadata, the failure is reported. There is no GENERIC_READ/GENERIC_WRITE fallback, permission change, configuration request, feature read, report write, input read, report-ID probing, polling loop, or device-setting modification. No HidD_GetFeature, HidD_SetFeature, ReadFile, WriteFile or DeviceIoControl import exists in this utility.

SafeFileHandle owns the interface handle. Dedicated SafeHandle types destroy the SetupAPI device-info set and free HID preparsed data; using scopes release them even after a capability error. The temporary SetupAPI detail buffer is freed in a finally block, and UTF-16 decoding is bounded by the allocation. The detail structure's cbSize is 8 on x64 / 6 on x86 Unicode, while DevicePath begins at byte 4 on both. HIDP_CAPS is 64 bytes. HidD BOOLEAN returns are marshaled as one byte; SetupAPI BOOL returns as four bytes. HidP_GetCaps succeeds with **HIDP_STATUS_SUCCESS (0x00110000)**, not Win32 zero. Structure sizes, the status constant and the metadata-only import whitelist have regression tests.

## Outcomes and error handling

| Exit code | Meaning |
| --- | --- |
| 0 | Metadata calls completed for the matching interfaces listed; does not promise all three collections were present |
| 1 | Enumeration/collection/API error, unexpected diagnostic failure, or clipboard failure after an otherwise successful run |
| 2 | Unsupported platform or unsupported command-line arguments |
| 3 | Enumeration completed without any matching collection interfaces |
| Other nonzero | PowerShell preserves a restore/build/runtime command failure; 127 means dotnet was not found |

Per-collection errors do not stop other matching collections from being inspected. Incomplete enumeration remains an explicit error even if some matches succeeded. Numeric error codes avoid including device paths from exception messages. Removing the receiver during the one-shot inspection can produce an expected open/preparsed-data error. Access denied is a reportable outcome; do not elevate, terminate G HUB or send reports as a workaround. A clipboard failure is identified separately without replacing a prior diagnostic error code.

## Local verification

Run all existing and new tests using the unchanged local runner:

```powershell
.\test.ps1 -Release -CopySummary
```

Hardware-independent inspector tests use synthetic interface paths and a fake metadata backend. They cover exact filtering/exclusions, formatting all counts/lengths including zero, expected/actual usage differences, multiple receivers, duplicate paths, no-match/missing results, partial errors, numeric status/error formatting, identifier redaction, native struct layouts and the P/Invoke whitelist. They neither invoke Windows HID APIs nor prove real driver behavior. Existing reader/monitoring/Windows form tests are preserved.

Then run `diagnose-g305.ps1 -CopySummary` on Windows with the candidate receiver attached. Check that COL01/COL02/COL03 appear if exposed, their actual usages match or are clearly flagged, and lengths/counts are numeric. Confirm the G915 C547 receiver is not listed, and neither output nor clipboard contains instance identifiers or user-specific paths. Run a second time to check that the one-shot process exits and can reopen the interfaces normally. Do not generate traffic to test capability lengths.

If convenient, run once with the candidate receiver absent (or on a machine without it): expect no matching interfaces / exit 3, without treating that as a battery/connectivity result. With several candidate receivers, check that duplicate collection numbers remain separate entries. Share the sanitized report and the actual full-test summary; no hardware success or Windows clipboard result is claimed until observed.

## Official references

- [SetupDiGetClassDevsW](https://learn.microsoft.com/windows/win32/api/setupapi/nf-setupapi-setupdigetclassdevsw)
- [SetupDiEnumDeviceInterfaces](https://learn.microsoft.com/windows/win32/api/setupapi/nf-setupapi-setupdienumdeviceinterfaces)
- [SetupDiGetDeviceInterfaceDetailW](https://learn.microsoft.com/windows/win32/api/setupapi/nf-setupapi-setupdigetdeviceinterfacedetailw)
- [CreateFileW](https://learn.microsoft.com/windows/win32/api/fileapi/nf-fileapi-createfilew)
- [HidD_GetPreparsedData](https://learn.microsoft.com/windows-hardware/drivers/ddi/hidsdi/nf-hidsdi-hidd_getpreparseddata)
- [HidP_GetCaps](https://learn.microsoft.com/windows-hardware/drivers/ddi/hidpi/nf-hidpi-hidp_getcaps) and [HIDP_CAPS](https://learn.microsoft.com/windows-hardware/drivers/ddi/hidpi/ns-hidpi-_hidp_caps)
- [HidD_FreePreparsedData](https://learn.microsoft.com/windows-hardware/drivers/ddi/hidsdi/nf-hidsdi-hidd_freepreparseddata)
