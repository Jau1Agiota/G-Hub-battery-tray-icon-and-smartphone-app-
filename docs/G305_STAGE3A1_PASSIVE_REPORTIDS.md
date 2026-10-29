# G305 Stage 3A.1 — passive capability ReportIDs

2026-10-28. Extends only the explicitly invoked G305 inspector. No live input/output/feature report is requested, consumed or transmitted. G305 battery remains Unknown. Stage 3B remains **NO-GO**.

## Owner-run command

Check out `feature/g305-stage3a1-passive-reportids`, then run from the repository root on Windows (no elevation, G HUB may remain open):

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\diagnose-g305.ps1 -CopySummary
```

The execution-policy override applies only to this PowerShell process. Requires the .NET SDK selected by global.json. The existing launcher restores/builds the dedicated inspector and copies its sanitized summary. Paste that summary for review; do not run a battery probe. Access refusal is a valid inconclusive result; do not close G HUB, force access or rerun with elevated rights to bypass it.

## Implementation and bounds

The G305 executable opts into ReportID metadata. Other callers, including HyperX native/POC/production paths, retain the backend's default OFF option; the option is also forcibly ineffective for the HyperX target. No launcher parameters, dependencies, packaging, providers, scheduler or tray code changed.

Exact existing VID 046D/PID C53F/MI_02/COL01–03 filtering occurs before opening any handle, including exclusion of G915 C547. Existing expected collection usages FF00:0001/0002/0004 are checked before extracting IDs; an unexpected usage retains the original caps and warning but yields no extracted IDs. Open is still desired access 0, FILE_SHARE_READ|FILE_SHARE_WRITE (3), OPEN_EXISTING (3). No fallback or retries.

On the same SafePreparsedData handle already acquired, HidP_GetButtonCaps and HidP_GetValueCaps parse cached capabilities for HidP_Input (0) and HidP_Output (1) only. Feature lengths/counts remain displayed, but feature capabilities are not queried: owner reports feature length 0. Arrays have exactly the count declared by HidP_GetCaps, zero-initialized with the Windows SDK 72-byte button/value structure sizes and ReportID at offset 2. Unused fields/unions remain opaque. USHORT in/out counts start at allocated length. Only HIDP_STATUS_SUCCESS and an exact returned-count/array-length match are accepted. Zero count skips the call; >256 entries per kind/direction is rejected before allocating/calling. No retry for buffer-too-small; no partial IDs survive errors. Distinct byte IDs are sorted, bounded to 256 possible values. Counts and numeric HID status are printed per kind/direction, never arbitrary buffer bytes. API failures, inconsistent counts and limits make the process exit incomplete (1); an empty successful array is not established, not an API failure. Existing no-device/platform/access error behavior remains.

Grouping uses SetupDiGetDeviceInterfaceDetailW's SP_DEVINFO_DATA plus at most eight cached CM_Get_Parent / CM_Get_Device_IDW ancestry steps, each with a 512-character local buffer. Only an exact USB VID_046D&PID_C53F&MI_02 ancestor is eligible. No live USB descriptor query or DeviceIoControl. Local IDs/DEVINST values never enter reports; equality maps to P1/P2/... per invocation. Missing/unsupported ancestry or failure yields “not established,” without guessed path trimming. Multiple parents remain separate and explicitly inconclusive. This groups USB interface ancestry, not paired wireless devices; labels can change across runs. These Configuration Manager calls are passive Windows PnP metadata additions solely to satisfy anonymous parent grouping.

All native device/preparsed/set handles retain using/SafeHandle ownership; unmanaged interface buffers retain try/finally. No pending native I/O or worker thread exists here. Button/value metadata parsing finishes before disposing preparsed data. No ReadFile, WriteFile, GetInputReport, GetFeature, SetFeature, report loop, receiver-slot scan, wake request or process termination is added.

## Illustrative output — NOT a hardware result

This is a formatting example with invented IDs, not a claim that this G305 has them:

```text
COL01
  Anonymous USB interface parent: P1 (invocation only)
  Usage page: 0xFF00; Usage ID: 0x0001
  Report bytes: input=7, output=7, feature=0
  Capability ReportIDs (not exhaustive descriptor IDs):
    Input buttons: not established; requested=0, returned=0; NotEstablished
    Input values: 0x10; requested=1, returned=1; Available
    Output buttons: not established; requested=0, returned=0; NotEstablished
    Output values: 0x10; requested=1, returned=1; Available
```

Actual output also retains prior scope, sanitized path placeholder, capability counts, link collection nodes, collection absence, errors and exit code. Numeric ID 0 is retained if present and may mean no report ID for that usage. Empty arrays, an access error, unsupported ancestry or a count limit is inconclusive; do not interpret it as proof that a report does not exist. Failure output includes only fixed steps/numeric codes, not native exception text or identifiers.

## What this cannot establish

These usage arrays may not represent every descriptor report. Even IDs matching general HID++ 0x10/0x11 framing do not prove protocol family/version, radio device slot, live G305 identity, feature availability/index/function, battery semantics, response ownership or freshness alongside G HUB. The 32-byte collection's purpose remains unknown. No value or percentage is decoded. See the [protocol review](G305_STAGE3A_PROTOCOL_REVIEW.md) and [updated Stage 3B evidence matrix](G305_STAGE3B_EXPERIMENT_PROPOSAL.md#stage-3a1-evidence-matrix).

API references: Microsoft [HidP_GetButtonCaps](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/hidpi/nf-hidpi-hidp_getbuttoncaps), [HidP_GetValueCaps](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/hidpi/nf-hidpi-hidp_getvaluecaps), [HIDP_BUTTON_CAPS](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/hidpi/ns-hidpi-_hidp_button_caps), [HIDP_VALUE_CAPS](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/hidpi/ns-hidpi-_hidp_value_caps). Original implementation; no third-party battery-monitor source used.

## Verification / changed files

Baseline Stage 3A: 72 focused tests passed. Stage 3A.1 Release solution build passed with zero warnings/errors. Focused passive G305/HyperX, SQLite/JSON and new ReportID tests: **126 passed, 0 failed, 0 skipped** (21 new ReportID cases and 105 existing cases). Linux unsupported-platform smoke check returned the expected exit 2 without native inspection. New tests cover distinct/zero IDs, zero/maximum/excess counts, malformed counts, failed status/partial arrays, Windows ABI sizes/offsets, exact USB ancestor matching/G915 exclusion, anonymous grouping/redaction and a strict new native metadata import allowlist. Existing metadata-only import tests remain unchanged. Full regression suite and Windows/physical/clipboard verification **NOT RUN**; owner will supply actual metadata. No hardware commands executed by Codex.

Changed implementation: G305Inspector/Program.cs; HidMetadata/PassiveReportIds.cs, WindowsHidMetadataBackend.cs, CapabilityInspector.cs, InspectionReport.cs, ReportFormatter.cs. Tests: PassiveReportIdTests.cs. Docs: this file, Stage 3B proposal and feasibility follow-up. AGENTS records the narrow passive authorization. No test assertions removed or weakened. Build/test commands use Release with locked existing dependencies/no restore, focused filter `PassiveReportIdTests|HidInspectorTests|HyperXInspectorTests|G305GHubInspectionTests` on FullyQualifiedName. `git diff --check` and production-path audit passed before push.

Branch: `feature/g305-stage3a1-passive-reportids`, based on Stage 3A `51e2821` (preserves both Stage 2 commits). Commit recorded by `git log --oneline 51e2821..HEAD`. [Compare with main](https://github.com/Jau1Agiota/G-Hub-battery-tray-icon-and-smartphone-app-/compare/main...feature/g305-stage3a1-passive-reportids?expand=1); comparison includes unmerged prior stages. No merge, release or branch deletion. Stop for owner summary and separate approval before active Stage 3B.
