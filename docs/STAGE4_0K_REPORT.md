# Stage 4.0K — dashboard and native tray tooltip

## Base and scope

Branch: `feature/stage4-0k-dashboard-tooltip`, created from actual latest main `14ad74c3c2018d120c19963d5bd038833be2d67b` (PR #4, merged F/H/I). The working tree was clean. The public PR dashboard listed zero open PRs; authenticated API listing returned Forbidden. Remote branches/main contain no Stage 4.0J implementation, so there is no unmerged J dependency to assume.

**Stage J reconciliation:** the current merged implementation persists the automatic-mode choice but still defaults off for new installations and requires session acknowledgement/preflight after restart. This UI stage preserves that behavior; it does not silently implement permanent/default-on monitoring or remove its safety gates. A new default-on policy remains separate work requiring its actual implementation/evidence, not a theme or rendering side effect. Existing 60/120/300-second user choices remain untouched.

## Visual implementation

The supplied concept is a visual reference, not an embedded screenshot. The native WinForms popup now has a drawn battery glyph/title/subtitle/settings header and three rounded, bordered dark/navy or light device cards. Local C# vector glyphs represent keyboard, mouse and headset, with colored card accents; no device photos, remote assets, new packages, animations or large binaries are used. Battery bars use antialiased rounded paths; every pen/brush/path is disposed. Card headings, percentages, update lines and lower actions share consistent spacing.

G915 retains its last-reported data and health colors, G305 remains Unknown/not supported with a neutral indicator, and HyperX uses the existing read-only compact view: `Battery estimate: NN%`, an estimated gold bar, update/cadence, concise failure/receiver/pause text, and an accessible info note/tooltip. Stale/unknown levels are unfilled; receiver detection never claims headset Online. Full source uncertainty, timestamps and diagnostic errors remain in Advanced.

Advanced is an explicitly expanded, initially hidden settings panel. It is reached from the header gear, lower Advanced settings button, or tray context menu. It retains interval/automatic preferences, session opt-in, manual query, applicable Cancel/Resume, full errors and Copy diagnostics, and adds System/Dark/Light theme selection. It collapses on dismissal/hide; Escape collapses before dismissing the panel. Native caption Close and lower Close keep the existing hide-not-exit behavior; application exit remains in the tray menu. Opening settings does not enable monitoring or query.

Theme selection persists separately in LocalApplicationData `PeripheralBatteryCompanion/dashboard-theme.txt`; it cannot overwrite HyperX preferences. System mode observes Windows AppsUseLightTheme and preference notifications; high contrast uses system colors. Explicit Dark/Light override normal system theme selection. Missing/invalid theme data defaults System; write errors are shown in Advanced. Theme rendering/tests can select non-persistently. Native controls retain keyboard focus cues; glyphs are decorative/non-tab-stop and battery accessibility descriptions retain estimate/stale meaning.

The form still owns its 360-DIP width and constrains bounds to the clicked monitor's working area. Card contents and nested labels wrap; longer settings content scrolls. Actual per-monitor DPI/font changes, scrollbar interaction, dropdown contrast and assistive technology remain owner-reviewed on Windows. No screenshot is claimed from Linux; the layout description is the implementation preview.

## Native hover tooltip

The pure cached-state `TrayTooltipView.Build` produces a bounded native NotifyIcon.Text value, for example:

```text
G915: 38%
HyperX: ~50%
G305: Unknown
```

`~` always marks an estimate, including stale estimates (`HyperX: ~50% stale`). Cached G915 values are marked stale; missing/invalid readings are Unknown. Fixed labels and validated 0–100 values fit within the conservative 63-character limit; a runtime guard falls back to complete Unknown lines rather than slicing percentages or uncertainty markers. Device names/private diagnostic text cannot enter this formatter.

Tooltip updates occur on the existing UI-thread G915/HyperX state updates, including metadata refresh/removal/reconnect; identical strings skip the native property assignment. There is no hover handler, extra timer, HID call, helper launch or refresh from the tooltip. Icon bitmap/meaning, click/double-click/right-click interaction, single NotifyIcon ownership and exit remain intact. The constructor's initial single-device text is replaced with the combined cached state during composition.

## Tests and boundaries

- Baseline: Stage I recorded 316 portable passes, 16 Windows-specific cases compiled.
- Release solution / win-x64 compilation: **PASS, zero warnings/errors** after fixing explicit designer-serialization declarations for programmatically owned custom-control colors.
- Portable suite: **333 passed, 0 failed/skipped** (17 new cases for tooltip length/full range/missing/stale/dynamic updates/private-name exclusion, theme resolution/text contrast and small/negative-coordinate screen placement). Existing provider, native fake, scheduler, SQLite and presentation cases retained.
- **20 Windows-specific cases compiled, not run on Linux**. New cases cover three theme choices/settings/card visibility/interval preservation and assigning worst-case text to a non-visible native NotifyIcon. Earlier scaled-text layout, lifetime simulations and UI cases retained; interval assertions now select the interval ComboBox explicitly because a separate theme selector exists.
- Debug win-x64 self-contained review artifact: `artifacts/PeripheralBatteryCompanion-stage4-0k-debug-win-x64.zip`, including the isolated `hyperx-helper` directory. Build/publish and the static helper isolation gate must pass before delivery. Keep the full folder together.
- Static diff confirms no changes to HyperX protocol/parsing, native cancellation, helper/IPC/process ownership, monitoring scheduling/backoff, process coexistence rules, monitoring preference store, consent orchestration, POC, G915 reader/monitor, tray icon renderer or `test.ps1`.

No physical USB query or hardware polling test was performed. No actual Windows rendering, tooltip hover, UI/screen-reader runtime, high-contrast or cross-monitor DPI validation is claimed. No merge or branch deletion is authorized.

## Owner-owned Windows checklist

1. Run existing `test.ps1 -Release -CopySummary` (or the documented execution-policy workaround) and Windows UI/process tests; verify the exact runner switches in `docs/TESTING.md`. The tests use fakes/no-HID simulations, not physical battery queries.
2. Inspect System/Dark/Light at 100/125/150/200% DPI, Windows text scaling, high contrast, small working areas and all monitor/taskbar edges. Check rounded cards, wrapped titles/status, no horizontal clipping, vertical scrolling and bottom actions. Verify selected theme persists independently of the interval/mode.
3. Use Tab, button mnemonics, Escape, native Close, gear/footer/tray settings entries, interval/theme dropdowns, and screen reader descriptions. Verify focus restoration, contrast, collapse on hiding, and that routine polling never activates the window or displays repeated prompts.
4. With existing approved data/monitoring only, check actual G915 values and last-reported time, unsupported G305, HyperX estimate/time/uncertainty, paused/gated/error states and stale receiver behavior. Never infer headset connectivity. Review physical monitoring separately under the existing checklist.
5. Hover the native tray icon: all three names/values fit, `~` remains for HyperX, cached values say stale, and reconnect does not make an old estimate current. Confirm hovering/showing/settings does not initiate a request or refresh.
6. Verify unchanged icon meaning, left click/double click toggle, right-click menu, one tray icon, Refresh semantics, application Exit cleanup and no late callbacks/disposed-control errors.
