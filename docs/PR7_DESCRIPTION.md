## DO NOT MERGE — owner approval required; keep this PR draft

Consolidates the existing Windows battery-companion work against main, preserving its linear research/stabilization history. No Android phase, new raw G305 HID query, new HyperX command or automatic merge/release.

### Included behavior

- Standalone bounded, sanitized, read-only G HUB SQLite investigation and passive G305 descriptor ReportID metadata, plus primary-specification/route review. Raw G305 Stage 3B remains NO-GO; research does not establish a mouse slot.
- Windows monitor shutdown/read-size/time safety and current dashboard validation.
- GET-only undocumented loopback G HUB gateway for dynamically discovered ACTIVE G915/G305, immediate startup and 60-second Logitech polls, correlated/fragmented/bounded responses and clean cancellation. SQLite retained for compatibility, not used as live fallback.
- Per-response SOC precision, approximate G305 labels and proportional bars; absent/failed/stale values remain honest. No remaining-runtime/precision invention.
- HyperX one initial eligible read after existing safe readiness, legitimate main Refresh, same OFF/suspension/backoff/single-flight/containment gates. Native helper/packet/receiver selection unchanged from main.
- HyperX secondary copy `ⓘ Estimate · source under validation`, with `Estimate; response source not uniquely verified` retained in tooltip/accessibility/diagnostics. No automated official-software comparison.

### Consolidated audit

[Full audit and all-file inventory](https://github.com/Jau1Agiota/G-Hub-battery-tray-icon-and-smartphone-app-/blob/feature/stage5.3-hyperx-confidence-copy/docs/PR7_CONSOLIDATED_AUDIT.md) · [Current Windows acceptance checklist](https://github.com/Jau1Agiota/G-Hub-battery-tray-icon-and-smartphone-app-/blob/feature/stage5.3-hyperx-confidence-copy/docs/PR7_OWNER_WINDOWS_CHECKLIST.md)

Initial review: 13 commits, 63 files, +3112/−78 at 67030bf against main 2948558. Audit adds documentation only. No confirmed BLOCKER/HIGH runtime defect; no speculative production change. Findings: historical instructions need current checklist, low-priority tooltip defensive-state consistency and metadata invocation-wide cap limitations, original-code distribution license pending. No added binaries/private captures/databases/secrets found by scoped checks; existing dependency versions/hashes and native protocol unchanged.

### Verification and outstanding gates

- Linux locked restore and Release solution build PASS, **0 warnings/errors**.
- **508 portable tests passed, 0 failed, 0 skipped**; **37 Windows-specific cases compiled, NOT RUN**. Expected combined Windows total **545**, not yet confirmed.
- Runner contract harness **15 PASS**; local publish/static isolation gate PASS; no app/helper hardware query launched.
- Owner reports Stage 5.2 Windows **542/542**, zero-warning build, working G915/G305, HyperX within 10 seconds, manual Refresh and near-60-second cadence. Stage 5.3 native copy/tooltip/layout and final-head full Windows verification remain pending.
- GitHub CI/review/draft metadata is unavailable to Codex due API Forbidden; no claim checks passed or approvals exist. Verify those in GitHub.

**Do not merge until owner executes the checklist at final head, reviews residual risks/CI and explicitly approves.** No force push/history rewrite, unrelated user files, public release or original-code license change.
