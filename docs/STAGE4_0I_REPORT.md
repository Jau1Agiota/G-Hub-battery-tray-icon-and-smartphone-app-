# Stage 4.0I: compact HyperX UI and consolidated delivery

The feature branch `feature/stage4-0i-hyperx-ui-polish` contains the complete unmerged sequence: Stage 4.0F `dbcbdea` (supervised helper), Stage 4.0H `550fc70` (optional automatic monitoring), then Stage 4.0I presentation changes. Actual remote main remains `4dbf1a3`. Target one draft PR at main; do not merge or delete the earlier branches. GitHub API listing returned Forbidden, so no existing PR number is assumed. See [Stage F design](HYPERX_NATIVE_TRANSPORT.md), [Stage H behavior/checklist](HYPERX_AUTOMATIC_MONITORING.md) and [consolidated PR body](HYPERX_F_H_I_PR_DESCRIPTION.md).

## Before / after layout

Before: the HyperX heading was followed by receiver/debug status, ambiguous-candidate paragraph, experimental checkbox, automatic preference checkbox, interval selector, next-check diagnostics, Resume, Query, Cancel and Copy diagnostics. These occupied the normal popup on every opening.

After, healthy example (illustrative, not a seeded runtime value):

```text
HyperX Cloud III Wireless
Battery estimate: 51%
[yellow estimated-level bar]
Updated: 17:43
Auto monitoring · every 60s
Estimate; response source not uniquely verified
HyperX settings
```

Unknown has no colored fill. Stale displays `Last estimate: 51% (stale)`, a neutral unfilled bar and `Last update: 17:43`; the full previous timestamp remains in its tooltip/accessibility description. Receiver absence, pause, disabled mode and errors use concise status text. A gated/suspended mode is never shown as active. No headset connection/charging/sleep state or ConfirmedFresh claim is added.

HyperX settings expands the existing controls in place, only on deliberate activation; it is collapsed by default and on panel dismissal. Cancel appears only during an operation; Resume only when suspended. Full exception/status/diagnostic content stays available under settings and Copy diagnostics. Closing settings returns focus to its button; Escape collapses settings first, then dismisses the normal popup on another Escape. The interval dropdown prevents focus-dismissal while open. Tab navigation and the settings mnemonic are preserved. Estimates have explicit screen-reader names/descriptions; the readonly HyperX level bar is not a tab stop.

The form still owns its width, uses DPI scaling and clamps to the working area with scrolling for long Advanced content. Hidden settings do not contribute the previous developer-control height. Nested diagnostic labels wrap to available width. G915/G305 content and tray icon semantics remain unchanged. No paint/layout code starts operations. The only Stage 4.0I composition change is passing a read-only compact view to the existing popup; no scheduler, transport, preference store, consent handler, protocol or Logitech reader changes are made.

## Cloud validation

- Baseline: Stage H recorded 299 portable tests and 11 Windows-specific cases compiled.
- Release solution build PASS, zero warnings/errors.
- Portable suite: **316 passed, 0 failed, 0 skipped**, including 17 new compact-view cases (full 0/100 range, unknown/stale/absent/paused/error, local timestamp, 60/120/300 cadence display, no consent/preferences mutation).
- **16 Windows-specific cases compile, not run on Linux**. Five new cases cover compact/Advanced visibility and preserved controls/preferences, plus scaled-text layouts at 96/120/144/192 equivalents. Scaled fonts are layout stress, not proof of actual per-monitor DPI changes.
- Existing Stage H/F tests are preserved. Static diff review confirms no Stage I changes to automatic scheduling, native/IPC/process lifecycle, protocol, NGENUITY rules, preference persistence, POC, Logitech providers, icon or `test.ps1`.
- Build a Debug win-x64 self-contained review package from the committed branch, including `hyperx-helper`, and run the static artifact isolation gate. Output: `artifacts/PeripheralBatteryCompanion-hyperx-f-h-i-debug-win-x64.zip`.

No Windows screen rendering, assistive-technology runtime, HID hardware query or sustained polling experiment was performed. No screenshot is fabricated; the before/after description above is the preview. Owner-reported 51%/60-second monitoring is context, not cloud hardware validation.

## Owner-owned acceptance checklist

1. Review the consolidated F/H/I PR and run existing `test.ps1`/clipboard workflow plus Windows UI/no-HID process tests.
2. At 100/125/150/200% real Windows scaling, inspect normal/expanded views, narrow working areas, bottom Refresh/Close reachability, keyboard Tab/Alt+S/Escape, focus restoration and screen-reader estimate descriptions. Drag between monitors and check no horizontal clipping. Confirm G915 and G305/tray behavior unchanged.
3. Confirm normal view hides experimental/query/cancel/diagnostic clutter, shows estimate rather than certainty, and preserves the already chosen interval and saved preference. Expansion/collapse/Refresh must never query or alter consent/cadence.
4. Only under separately approved hardware monitoring, confirm existing 60-second operation continues, receiver removal marks stale and reconnect follows the existing schedule. Preserve Stage H limitations: failed/uncertain cleanup remains gated; active-query unplug may require review/restart. Stop on audio/application disruption. Matching NGENUITY percentages do not prove unique response ownership.

No automatic merge, branch deletion or additional physical query is authorized by this UI stage.
