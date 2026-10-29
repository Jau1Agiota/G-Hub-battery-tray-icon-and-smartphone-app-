# Stage 5.3 — HyperX confidence copy (presentation only)

2026-10-29. Dedicated branch `feature/stage5.3-hyperx-confidence-copy`, direct base `870a9f9b2ba7c84c777b550fa272b279ca782cc2`. No merge, release or history rewrite. Existing unrelated dirty/untracked Stage 5 documentation is preserved and excluded; no JS/PublishProfiles/artifact files staged.

## Exact before / after and rationale

Main value remains **`Battery estimate: 76%`** for the example; actual percentage remains runtime data.

- Before secondary line: `ⓘ Estimate · source not uniquely verified`
- After secondary line: `ⓘ Estimate · source under validation`
- Unchanged technical note/tooltip: `Estimate; response source not uniquely verified`

Neutral copy was chosen because it appears for every installation. The owner reports repeated agreement with the official HyperX app; that is historical owner-reported manual testing, not automated comparison, every user's verification, proof of unique source ownership or guaranteed freshness. No “matches official software”/“user-verified” claim is placed in the universal UI. No validation setting/flag added.

`HyperXCompactView.FriendlyEstimateNote` holds the shorter copy; its computed `SecondaryText` applies only when no operational status overrides the estimate note. `BatteryPopupForm.DisplayHyperXCompact` uses that presentation property. Missing receiver, pause, OFF and failure status retain precedence. The main estimate/stale/unknown labels are unchanged.

The same full uncertainty remains on the estimate tooltip, secondary status tooltip, accessibility description and existing Advanced diagnostics. No candidate is promoted from CandidateAmbiguous, no raw/native/API flags or source semantics changed, and no live software comparison occurs. G915/G305 strings/precision and all battery-reading/safety behavior are untouched.

## Changes and validation

Source: Core/Presentation/HyperXCompactView.cs (copy/property); Windows/BatteryPopupForm.cs (secondary-line rendering only). Tests: HyperXCompactViewTests.cs (neutral wording, retained technical uncertainty and operational-status precedence); Windows.Tests/PopupLayoutTests.cs (actual label, font-width/height fit, accessible uncertainty and original tooltip). Docs: this report and [Windows checklist](STAGE5_3_OWNER_CHECKLIST.md).

Release solution build: **PASS, zero warnings/errors**. Portable tests: **508 passed, 0 failed, 0 skipped** (two new cases over Stage 5.2's 506). **37 Windows-specific cases compiled, NOT RUN** on Linux (one new native UI assertion). A first compilation caught an xUnit analyzer rule in the new test; the assertion was changed to the supported predicate overload, not suppressed. No existing assertions removed.

Commands used with existing SDK 10.0.403/dependencies:

```text
dotnet build PeripheralBatteryCompanion.sln -c Release --no-restore --nologo --verbosity quiet
dotnet test tests/PeripheralBatteryCompanion.Tests/PeripheralBatteryCompanion.Tests.csproj -c Release --no-build --no-restore --logger "trx;LogFileName=stage53-final.trx" --verbosity quiet
```

Native width/tooltip tests compile but do not establish screenshot fit until run on Windows. Real clipboard, DPI/theme rendering and hardware tests **NOT RUN** here. No gateway connection or HID operation executed. Diff/links checks and protected-source audit: Logitech provider/model/presentation strings, HyperX provider/startup/scheduler/backoff/helper/native/protocol, storage, commands and package/scripts unchanged. No dependencies added. The shared WinForms file changed only the HyperX secondary-line assignment.

## Handoff

[GitHub branch](https://github.com/Jau1Agiota/G-Hub-battery-tray-icon-and-smartphone-app-/tree/feature/stage5.3-hyperx-confidence-copy). Commit reported in delivery and listed with `git log --oneline 870a9f9..HEAD`. Run Windows tests and capture the real full HyperX card per checklist; verify the new short line is readable and hovering retains the precise technical uncertainty. No hardware behavior change or automatic merge/release. Stop for owner review.
