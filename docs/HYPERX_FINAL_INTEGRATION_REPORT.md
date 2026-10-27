# Final HyperX integration report

## Branch and consolidation audit

Final branch: `feature/hyperx-final-integration`. Actual remote HEAD/default target: **main**, `2b64edc34c59ee8925acb325f5be7fb1d0cafbc9` at inspection. This main already includes Stage 3.1 popup, Stage 3.1.1 UI/WAL corrections and Stage 4.0A through merged PR #2; those commits were not replayed.

All remote heads were fetched explicitly (the original remote fetch configuration only tracked main). HyperX work was found on the Stage 4.0B and Stage 4.0C branches; there was no remote Stage 4.0E branch/implementation. The public GitHub PR dashboard showed **0 open / 2 closed** PRs; Git pull refs exposed #1/#2, both already merged in main. Authenticated REST/GraphQL inspection returned transport `Forbidden`, so public dashboard plus Git ancestry were used for the audit. No existing branch or PR was deleted/closed.

The final branch was created at origin/main, then fast-forwarded through the existing linear HyperX chain. No cherry-pick duplicates, conflicting implementation or extra merge commit was introduced. Included historical commits, in order:

| Commit | Included work |
| --- | --- |
| d7a496b | Stage 4.0B passive HyperX profile, launcher, tests |
| bd0733e | Isolated fixed-payload one-shot POC |
| 9e859c0 | Owner hardware percentage comparison and capture procedure |
| 63115f8 | Owner verified wire transfer record and coexistence plan |
| a9a2a33 | Passive shared-handle coexistence mode |
| 8ca502e | Separately acknowledged active coexistence experiment |

The final integration commit adds Stage 4.0E and this audit; use Git history/delivery for its hash. Submit one **draft** PR against main, prefer **squash merge after final review**, and do not merge automatically. Draft creation is attempted after push; see delivery for the actual API result/link. The local PR description is [HYPERX_FINAL_PR_DESCRIPTION.md](HYPERX_FINAL_PR_DESCRIPTION.md).

## Stage 4.0E implementation and explicit blocker

The tray has a separate HyperX receiver/battery section and metadata-only discovery at startup and explicit Refresh. Receiver presence does not assert headset connectivity. The per-device provider uses existing BatteryDevice with optional state/source/presence fields; the keyboard IBatteryProvider/monitoring contract is unchanged. States distinguish Unknown, Unavailable, Stale, CandidateAmbiguous and reserved ConfirmedFresh; no HyperX path emits ConfirmedFresh or seeds an observed 56%.

Experimental preference defaults off, is session-only and requires a warning acknowledgement. **Active querying remains hard-disabled even after opt-in.** No active native transport is wired into the tray, because the POC's standalone process-exit fallback is not evidence of safe lifetime in a long-running tray. This is the approved briefing's explicit safety fallback, not a claim that an active production reader is complete. Manual orchestration (opt-in/lifetime/target/process/single-flight/deadline/failure-stop) is implemented using fake transports in tests. Production native activation and its hardware validation remain blocked work for a separate design/review.

The seven existing passive source files moved byte-for-byte into the HidMetadata class library. This was necessary after an initial build exposed NETSDK1150: a self-contained executable cannot reference a framework-dependent executable. Namespaces/entry points remain compatible. A pure HyperXCollectionRules helper is shared by the POC selector and production metadata adapter, avoiding conflicting target rules. The tray references only the passive metadata library, never the POC; the published dependency graph was checked for that boundary. No new package or native report import was added.

Changed implementation groups: Core device fields/provider/presentation; passive class-library extraction and project/lock references; Windows metadata/process adapters, popup section, opt-in warning and shutdown coordination; shared POC selection delegation. Documentation and synthetic/Windows layout tests accompany these changes. Detailed behavior/risks are in [HYPERX_INTEGRATION.md](HYPERX_INTEGRATION.md).

## Verification observed in the cloud

- Frozen solution restore: **passed**.
- Release solution compilation: **passed, 0 warnings, 0 errors**.
- Entire portable Release suite: **229 passed, 0 failed, 0 skipped** (200 pre-integration cases retained + 29 new provider/discovery cases). TRX generated locally; no hardware USB exchange in tests.
- Existing PowerShell verification-runner contracts: **15 passed**. `test.ps1` and clipboard behavior remain unchanged.
- Windows Forms tests: **5 compiled, not run** on Linux (3 prior + 2 new HyperX layout/control cases). Windows full Release suite, actual tray/DPI behavior and clipboard remain owner checks; no remote CI success is claimed.
- Debug Windows x64 folder publish: **passed**, portable PDBs/notices included, no active POC assembly in dependency graph. Local ZIP: `artifacts/PeripheralBatteryCompanion-HyperX-debug-win-x64.zip`; build outputs are ignored, not source commits. Debug execution requires Windows; it was not launched here.
- All seven moved metadata source files verified identical to 8ca502e. G915 SQLite reader/monitor, POC native session/coordinator, test.ps1 and diagnostic launchers verified unchanged. Package lock audit found no existing NuGet version/content-hash changes; only project-reference graph entries changed.
- Diff whitespace checks: **passed**. Full consolidated ancestry/diff inspected; existing UI width/WAL corrections remain in target ancestry.

Earlier intermediate build errors were corrected (self-contained/executable reference and target-typed Task.Run return inference). Final results above are from the corrected tree. No tests/assertions were removed or weakened, and no hardware query/capture was run by Codex.

## Hardware evidence and remaining review

The owner supplied verified request setup/data, exact matching input, an independent 56% NGENUITY comparison, successful passive shared access, and one successful active coexistence trial on 8ca502e (56%, 77.5 ms, nominal NGENUITY/audio). This is evidence of that observation, not unique correlation, fresh connection state or reliable continuous operation. Bytes 2/3 remain unknown. The current integration adds no new physical evidence.

Before promotion: run the local Windows Release suite and non-query UI checklist, review long-running I/O cancellation/resource ownership and a safe native transport architecture, preserve explicit receiver/link distinction and ambiguous candidate labels, and separately approve each future physical attempt. No polling, retries, access escalation, service/driver changes or Android work is included. Do not use the retained POC commands merely to validate this consolidation; new battery requests remain unauthorized.
