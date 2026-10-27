# Consolidate HyperX stages 4.0B–4.0E with guarded tray integration

Main currently lacks the HyperX work accumulated on the research/POC branches. This draft consolidates that linear history and adds a production HyperX device section that discovers receiver metadata without querying the battery. G915 monitoring, G305 behavior, popup width/WAL fixes and diagnostic entry points are preserved.

Included: Stage 4.0B passive inspector; Stage 4.0C fixed-request POC, percentage comparison and USBPcap wire verification; passive shared-handle test; Stage 4.0D acknowledged active coexistence experiment; Stage 4.0E provider/state/UI and fake-tested manual orchestration. The owner's single concurrent 56% trial matched NGENUITY and left audio/application nominal, but its response remained ownership-ambiguous. That does not establish freshness or continuous polling reliability.

## Production safety and deliberate blocker

- Default and startup behavior send zero HyperX battery requests. Discovery/Refresh read metadata only; receiver presence never claims the headset is connected.
- Battery states distinguish Unknown, Unavailable, Stale, CandidateAmbiguous and reserved ConfirmedFresh. No hardcoded 56% startup data, and no HyperX path emits ConfirmedFresh.
- A session-only experimental opt-in requires acknowledgement. The query button **remains disabled after opt-in**: no native active transport is wired into the tray until completion-safe long-running lifetime is demonstrated. This follows the Stage 4.0E briefing's safety fallback; active production telemetry remains a blocker, not a completed/validated feature.
- The manual controller is tested with fakes for opt-in/lifetime/process/target guards, single-flight, three-second exchange deadline, ambiguous candidates, stale/disconnected state, cancellation/disposal and stopping after failure. No retry, polling, queue drain, initializer/feature command or permission fallback.
- The standalone closed-NGENUITY and explicitly acknowledged coexistence POC modes remain separate. No production POC invocation or active assembly dependency was added.

Passive code was extracted unchanged into a shared class library because the self-contained tray cannot reference the framework-dependent inspector executable. Exact HyperX selection/capability rules are now shared. No new NuGet package versions or native report imports.

## Verification

- Locked restore and Release solution build passed, 0 warnings/errors.
- 229 portable Release tests passed; 15 existing verification-runner checks passed.
- Five Windows layout tests compiled but were not run on Linux. Windows full suite/tray/DPI/clipboard remain owner checks; remote CI results not yet observed.
- Debug Windows x64 folder publish passed, with portable PDBs/notices; published dependency graph excludes the active POC.
- Reviewed consolidated diff/ancestry; G915 reader/monitor, POC native I/O/coordinator, verification script and diagnostic launchers unchanged. No hardware request was executed during implementation/testing.

See [integration behavior and manual checklist](docs/HYPERX_INTEGRATION.md) and [consolidation audit](docs/HYPERX_FINAL_INTEGRATION_REPORT.md). Long-running lifetime proof, response correlation and further reliability/non-interference checks remain separate review/validation gates. Every additional physical query needs explicit owner approval.

Draft for final review. **Prefer squash merge into main**, leaving one integration commit. Do not merge automatically or delete the existing branches as part of this PR.
