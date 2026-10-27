# Stage 4.0B: HyperX Cloud III Wireless passive HID inspector

This one-shot diagnostic adds a fixed HyperX profile to the existing Stage 4.0A metadata inspector. It does not decode battery data, start a production provider, sample input reports, send commands, change audio settings/drivers, or control NGENUITY. The original G305 launcher and its default scope remain unchanged.

## Supplied hardware findings

These identifiers came from the owner's Windows hardware enumeration; they are not new hardware results from this implementation:

| Property | Supplied value |
| --- | --- |
| Device | HyperX Cloud III Wireless |
| Vendor/product | `03F0:05B7` |
| USB hardware-ID revision | `4109` |
| Interface | `MI_03` |
| COL01 (primary research candidate) | Vendor-defined, `FF13:0001` |
| COL02 | Consumer control, `000C:0001` |
| COL03 | Headset, `000B:0005` |

The owner reported all three collections connected and operational. The utility independently reports whichever matching HID interfaces are currently enumerated; this is not proof of the wireless headset's connectivity or battery state.

Filtering requires exact case-insensitive VID, PID, MI and collection tokens. Different interfaces, devices and substring lookalikes are excluded before opening handles. Optional `REV_4109` hardware-ID tokens are accepted but not required: HID symbolic paths may omit the USB revision. A different revision does not exclude an otherwise matching receiver. The report labels `4109` as **supplied reference information, not queried firmware**, and does not infer an active firmware version from it.

## Run on Windows

Use a normal Windows 11 PowerShell session with the SDK selected by `global.json` on PATH:

```powershell
.\diagnose-hyperx.ps1 -CopySummary
```

Without `-CopySummary`, print the same sanitized report to the terminal only. Calling the script through a quoted absolute path works from another directory, including paths containing spaces.

If script execution is blocked:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\diagnose-hyperx.ps1 -CopySummary
```

For PowerShell 7 use `pwsh.exe` instead. The override applies only to that process; no machine/user execution-policy setting is changed. The script retains Windows PowerShell 5.1-compatible syntax.

The launcher performs a locked Release restore/build of the existing independent console project, then runs its DLL with the closed `--hyperx` profile selector. The historical project/namespace name remains `PeripheralBatteryCompanion.G305Inspector` to avoid unnecessary project or deployment changes. The executable with no arguments still selects **G305**. It accepts no arbitrary VID/PID, input-capture or report-transmission options. The new launcher does not call or alter `diagnose-g305.ps1`.

For G305 comparison, run the existing command separately:

```powershell
.\diagnose-g305.ps1 -CopySummary
```

Each clipboard command replaces the clipboard; paste/save each sanitized result before running the next command.

## Reading the report

HyperX COL01 is inspected first, even if SetupAPI enumerates it after the other collections. COL02 and COL03 are also inspected and displayed for comparison. Each observed entry includes:

- A sanitized interface-path template containing VID/PID/MI/COL; opaque machine/device instance identifiers are omitted.
- Actual usage page and usage ID in hexadecimal. Differences from the supplied expectation are flagged without replacing the actual metadata.
- Input/output/feature report byte lengths from HIDP_CAPS, including a report-ID byte where applicable.
- Input/output/feature button-capability, value-capability and data-index counts, plus link collection node count.
- A numeric Win32 error or HID NTSTATUS if a metadata operation fails.

Zero lengths/counts are valid. Capability descriptor counts are not decoded reports or physical button counts. Synthetic test fixtures include invented lengths solely to test formatting; no numeric length or count is claimed as a HyperX hardware finding until the owner runs the inspector.

A missing collection is marked **not observed**. A collection that was found but could not be opened/read has an explicit **ERROR**, so access denied is not mistaken for absence. A failure on priority COL01 does not prevent inspecting other matching collections. Incomplete enumeration stays an error even if some data is available. Multiple receivers produce separate numbered entries rather than being merged by collection number; their raw instance identifiers and physical parent relationships are not exposed or inferred.

Exit codes retain Stage 4.0A semantics:

| Code | Meaning |
| --- | --- |
| 0 | Metadata inspection completed for the matching interfaces listed; does not imply all three exist |
| 1 | Enumeration/inspection/API failure, or clipboard failure after an otherwise successful diagnostic |
| 2 | Unsupported platform or unsupported command-line arguments |
| 3 | No matching receiver interfaces found |
| Other nonzero | Native restore/build/runtime failure preserved by the launcher; 127 means dotnet was missing |

A nonzero feature/output/input length describes a declared report capability. It does **not** establish a battery field, a safe command, the meaning of a report ID, or whether the receiver emits unsolicited telemetry. COL01's vendor-defined usage makes it the first comparison candidate, not a verified battery interface. Suitability for battery telemetry remains unresolved until actual results and separately approved research provide evidence.

## Passive access and resource ownership

Both profiles use the same SetupAPI enumeration, HID preparsed-data reader and formatter. Device handles always use **desired access 0**, **share read/write**, and **OPEN_EXISTING**. There is no broader-access fallback or exclusive device access. Handles, preparsed data and temporary unmanaged buffers retain the existing SafeHandle/using/finally ownership.

The native interop module is unchanged: its only imports are HidD_GetHidGuid; SetupDiGetClassDevsW/EnumDeviceInterfaces/GetDeviceInterfaceDetailW/DestroyDeviceInfoList; CreateFileW; and HidD_GetPreparsedData/HidP_GetCaps/HidD_FreePreparsedData. There is no ReadFile, WriteFile, HidD_GetFeature, HidD_SetFeature, HidD_SetOutputReport or DeviceIoControl import. No audio APIs, driver changes, registry writes, device configuration, application termination or network access are introduced. See [the shared API/layout contract](G305_HID_INSPECTOR.md#api-and-access-contract) and its linked official Microsoft documentation.

Access errors are diagnostic outcomes: report them rather than elevating, closing NGENUITY, reconfiguring audio, or sending a command as a workaround. The report model carries only fixed profile identifiers and numeric metadata/errors, never raw HID paths or exception messages containing instance IDs. Terminal and clipboard use the same sanitized formatter; no diagnostic files are written.

## Verification and owner handoff

Run the complete local suite on Windows:

```powershell
.\test.ps1 -Release -CopySummary
```

This includes the existing G305, SQLite/monitoring, popup-state and Windows Forms tests, plus new synthetic HyperX coverage. The new tests cover filtering/MI/COL/revision behavior, priority ordering, all three usages and capability fields, missing versus failed collections, partial errors, redaction, unexpected usage values, duplicate/multiple receivers, CLI selection and default G305 preservation. They do not open real HID devices or prove hardware behavior.

Then:

1. Run `diagnose-hyperx.ps1 -CopySummary` with the receiver present. Check COL01 is first and all observed collections have actual usages, lengths and counts or explicit errors.
2. Verify COL01/COL02/COL03 against the supplied usage table, retaining any differences as findings. Check `4109` is labeled reference-only, not a queried firmware result.
3. Paste the sanitized report and confirm neither terminal nor clipboard includes instance identifiers or user-specific paths. With multiple matching receivers, expect separate entries.
4. Run the diagnostic again to check normal process completion/reopening, without changing audio settings or NGENUITY. Verify ordinary audio/NGENUITY operation remains unaffected on the owner's machine; this has not been tested in Linux.
5. Run the G305 launcher independently. Confirm its old `046D:C53F / MI_02` scope and report remain intact and HyperX is not included there.
6. If convenient on a machine without the HyperX receiver, verify no-match / exit 3. Do not interpret this as a wireless connectivity or battery report.
7. Share the actual full-test summary separately from the hardware reports. Mark unrun native/hardware checks NOT RUN.

Linux can compile the solution and execute the hardware-independent suite, but cannot run the existing Windows Forms test assembly or native HID APIs. Passing synthetic tests and metadata formatting do not count as physical receiver verification.

## Stage 4.0C boundary

No input capture or decoding is implemented. After actual Windows hardware results, any proposed unsolicited-report capture must be separately reviewed, bounded, non-invasive and opt-in. Battery decoding must be verified against an independent reference before any production monitoring/tray integration. Declared capabilities alone are insufficient evidence to begin that work automatically.

## Shared metadata extraction in final integration

The executable name, closed profile selectors and launcher behavior remain unchanged. Passive implementation types now reside in the HidMetadata class library with their original namespace, so the self-contained tray can share them without referencing the inspector executable. No additional report API or active behavior was added to this inspector. See [production integration boundaries](HYPERX_INTEGRATION.md).
