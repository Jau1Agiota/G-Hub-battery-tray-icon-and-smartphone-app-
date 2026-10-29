# PR #7 — consolidated audit

2026-10-29. **DO NOT MERGE until explicit owner approval. Keep PR draft.** [PR #7](https://github.com/Jau1Agiota/G-Hub-battery-tray-icon-and-smartphone-app-/pull/7). This is an audit of the complete main-to-feature diff, not just Stage 5.3. No BLOCKER/HIGH runtime defect confirmed; no production code or safety behavior changed by this audit. Findings are scoped conclusions, not proof of absence of all bugs.

## Identity, ancestry and scope

Verified main `2948558cd2f39c4fde8e2b9fe3aab31c318d5c3a`, head/PR git ref `67030bf8117656589ec1058badce9a2238475b43`, branch `feature/stage5.3-hyperx-confidence-copy`. Initial diff exactly **13 commits, 63 files, 3112 additions / 78 deletions**, main ancestor, no divergence. Existing three dirty/untracked Stage 5 documentation paths were preserved/excluded; Windows research JS/PublishProfiles absent in cloud, not touched. Audit adds only three new documentation files on the same PR branch; no new PR, merge, force push, release or tag.

Initial 13 commits, newest first: 67030bf, 870a9f9, 612e7cb, 7613ab2, ac0b98f, 773cae5, 7d3cbb9, e065251, 20b0041, 12da59c, 51e2821, 1af72a0, 69b607a. Includes standalone SQLite/passive HID research, Stage 4 stabilization, G HUB gateway, Logitech bars/precision, HyperX first-read/Refresh and confidence wording. Main already contains the earlier supervised helper/dashboard; this PR does not introduce a new native protocol.

## Severity-ranked findings (recorded before edits)

| ID / severity | Evidence and realistic scenario | Disposition |
|---|---|---|
| No BLOCKER/HIGH confirmed | Runtime paths and full portable tests below; owner Stage 5.2 evidence is positive but Stage 5.3 Windows validation remains pending | No speculative runtime fix or safety redesign. Merge remains owner-gated. |
| A1 MEDIUM — historical validation instructions | `docs/TESTING.md:124–132` still describes G305 unsupported, SQLite report semantics, missing Advanced menu and 360-pixel/two-section layout. Stage 4 historical checklist also requires G305 Unknown. Following those as current acceptance would validate/reject the wrong behavior. The tracked footer identifies some history, but not the full current integration. | Added authoritative [consolidated owner checklist](PR7_OWNER_WINDOWS_CHECKLIST.md) and PR description. Historical stage reports preserved. The unrelated dirty TESTING.md addition was not staged/overwritten. Use the consolidated checklist for PR #7. |
| A2 LOW — defensive presentation mismatch | `src/PeripheralBatteryCompanion.Core/Presentation/TrayTooltipView.cs:10–23` accepts Available + numeric value; panel's HasCurrentValue additionally excludes explicit Stale/Unavailable observation and Absent connection. In an inconsistent synthetic state a tooltip could show a current number while the panel rejects it. Current gateway paths do not emit that contradictory tuple; no live defect reproduced. | Residual hardening candidate, not a HIGH or a reason to alter validated runtime in this audit. Any future fix should share/test the predicate and preserve the 63-character budget. |
| A3 LOW — invocation-wide metadata cap | `src/PeripheralBatteryCompanion.HidMetadata/WindowsHidMetadataBackend.cs:22–30` enumerates the present interface set until ERROR_NO_MORE_ITEMS, without a separate total cap. Report-cap arrays are capped at 256 and parent walk at eight ancestors. Thousands of synthetic virtual interfaces could prolong explicit diagnostics/output. | No demonstrated production failure. Documented limitation; no new device operation or speculative native change. A future bounded-enumeration change needs synthetic limit/error tests. |
| A4 NOTE — distribution license | `THIRD_PARTY_NOTICES.md:3`: original-code license awaits owner approval. Dependency notices are present. | Existing release/distribution decision, not a newly introduced dependency/license defect. No license selected or public release published; owner must resolve before distribution. |
| A5 NOTE — GitHub state unavailable | GraphQL PR read, REST PR read and Actions list return Forbidden. Git PR head is readable, which proves commit identity but not draft/review/check state. | Draft status is owner-reported; current draft flag, approvals and CI conclusions cannot be independently confirmed. No assumption that CI passed. PR metadata update attempt/result below. |

Line references are at review head 67030bf. A2/A3 are specific static limitations; they are not claims of observed physical failures. No uncertain hardware/protocol fix was attempted.

## Runtime and safety review

**G305 research / SQLite diagnostic:** separately invoked Windows-only launcher; SQLite ReadOnly, pooling off, short timeout, connection-local allocation limit, same-snapshot latest record, bounded structural JSON. No DB/WAL copy, configuration write, process inspection or production fallback. Paths/types/fixed identifiers only in report; arbitrary values, machine paths, exception text and serials omitted. G305 cached config slotId remains explicitly not a proven HID route. Full primary-spec review is preserved, raw Stage 3B remains NO-GO; gateway support does not approve HID experiments.

**Passive HID metadata:** exact VID/PID/MI/COL filtering before zero-desired-access shared opens; unexpected usages are retained as metadata but not used for ReportID extraction. Preparsed/set/file ownership uses SafeHandle/using/finally. New APIs are button/value-cap parsers and cached PnP ancestry only, input/output metadata arrays 72-byte ABI/ReportID offset 2, count cap and exact status/count checks. IDs/groups are numeric/anonymous; 0 and absent IDs remain inconclusive. No ReadFile/WriteFile/feature report/DeviceIoControl path added. HyperX callers do not opt into G305 extension.

**G HUB gateway:** fixed loopback ws://127.0.0.1:9010 with json, proxy/default credentials off, known GET paths only, no configurable remote/control console/subscription. Per-refresh connection/discovery, ACTIVE+capability/model selection, dynamic sanitized IDs, duplicates/conflicting identity rejection. Reply correlation requires msgId/path/deviceId/SUCCESS; unsolicited/out-of-order traffic cannot become a result. Text fragmentation, unique JSON keys, depth/byte/fragment/message/device-count limits, valid percentage/optional fields, finite total 12-second cancellation budget and abort/dispose. No queued retry/spin or permanent reader. Current failures return no fresh value; caches stay separately stale. Partial malformed data conservatively rejects the poll. No direct network connection was exercised in the audit.

**Precision / presentation:** explicit per-device SOC_SUPPORTED means Percentage even when discovery useRealSoc=false; NOT_SUPPORTED/unknown remains qualified. Approximate valid values fill proportionally without claiming exactness/endurance. Invalid, absent, unsupported or stale panel values never fill. SQLite provider retained but not a live fallback. Gateway timestamps mean observed response time, not guaranteed measurement freshness. Tooltip uses fixed labels/bounded numbers; worst-case tests preserve ~ and stale within 63 characters. HyperX secondary text exactly `ⓘ Estimate · source under validation`, while tooltip/accessibility retains `Estimate; response source not uniquely verified`. Main value is still Battery estimate; no official-software automatic comparison or certainty promotion.

**HyperX changed paths:** one initial pending attempt after real receiver/preflight readiness, no first-interval delay when eligible; main Refresh now enters coordinator, not metadata-only path. OFF stays metadata-only, suspension requires Resume, persisted backoff gates initial/manual requests, readiness failures/unsafe cleanup do not retry or erase markers. Subsequent completion-relative cadence/reconnect/2–5–15 backoff/fourth-failure stop unchanged. All routes share coordinator/monitor/provider gates; helper/native protocol, target, 3-second exchange and supervision remain byte-identical to main. Startup/task work is off UI, and shutdown cancels/joins owned tasks before UI disposal. No native lifecycle guarantee is inferred from fakes; kernel exit can remain unconfirmed and latched.

## Build, tests, dependencies and static review

- Locked restore **PASS**; Release solution build **PASS, 0 warnings/errors**.
- Portable suite **508 passed, 0 failed, 0 skipped**. No new tests needed for documentation-only audit; existing negative/correlation/precision/bar/startup/preflight/backoff/cancel/late-result cases retained.
- **37 Windows-specific cases compile, NOT RUN** on Linux. They exercise STA controls/paint/fit/tooltips and no-HID process simulations, not physical devices. Expected combined total remains **545**, not confirmed Windows results.
- Runner contract harness **15 checks PASS**; native clipboard NOT RUN. Local self-contained publish and static helper/isolation/private-artifact gate results below. No executable launched.
- Existing dependency records/versions/content hashes unchanged; one new standalone diagnostic lockfile uses the same pinned Sqlite/SQLitePCLRaw dependencies. No framework/package upgrade, x86 churn, runner/CI suppression or new runtime package. global.json 10.0.402 rolls to installed SDK 10.0.403; runtime remains 10.0.14.
- All 63 changed paths inventoried below. Added files are text source/tests/docs/locks/scripts, no added binaries, captures, DBs, minified JS, PublishProfiles or credential-file candidates. Static private-key/token-marker scan found no hits in changed tracked contents. This is not an exhaustive secret-history guarantee.
- Committed local Markdown links pass. Unchanged package scripts include notices/licenses, isolated helper, and reject DB/capture files; standalone G305 diagnostic is not a tray publish dependency.
- CI workflow unchanged: Windows runner contracts → test.ps1 -Release → static-gated publish → artifacts. Current CI, reviews, mergeability and draft flag remain **unavailable**, not PASS/PENDING claims.

Actual commands:

```text
git fetch origin main
git fetch origin refs/pull/7/head:refs/remotes/origin/pr7-head
git log --oneline origin/main..HEAD
git diff --stat origin/main...HEAD
dotnet restore PeripheralBatteryCompanion.sln --locked-mode --verbosity quiet
dotnet build PeripheralBatteryCompanion.sln -c Release --no-restore --nologo --verbosity quiet
dotnet test tests/PeripheralBatteryCompanion.Tests/PeripheralBatteryCompanion.Tests.csproj -c Release --no-build --no-restore --logger "trx;LogFileName=pr7-audit.trx" --verbosity quiet
pwsh -NoLogo -NoProfile -File scripts/Test-VerificationRunner.ps1
pwsh -NoLogo -NoProfile -File scripts/Publish-Windows.ps1
```

The repository SDK/cache and PowerShell 7.5.4 were used on Linux. No audit build/test command launches HID or the real app. Owner Windows Stage 5.2 reported 542/542, zero warnings, HyperX within 10 seconds, manual success and ~60-second updates; retained as owner evidence only. Stage 5.3 native wording/tooltip/layout and full 545-case execution remain pending owner confirmation.

## Decision / owner gate

No confirmed runtime BLOCKER/HIGH fix warranted. Audit commit is documentation only; no new tests or altered APIs. A1 is addressed by the current checklist/description; A2/A3 remain explicit low-priority limitations, A4 a release decision. **Do not merge** until owner executes [the consolidated checklist](PR7_OWNER_WINDOWS_CHECKLIST.md), verifies exact final head and GitHub checks/reviews, and explicitly approves. No Android work started.

PR description source: [PR7_DESCRIPTION.md](PR7_DESCRIPTION.md). GitHub API update/draft attempt outcome and packaging result are recorded at the end of this report. This report and proposed body do not themselves change GitHub state.

## Initial 63-file review inventory

Categories: 25 source/project/lock paths, 12 test/project/lock paths, 23 docs/research, 2 build/policy, 1 script. Each item belongs to the complete main-to-67030bf review, not the unrelated working-tree files.

| Path | Review scope / disposition |
|---|---|
| `AGENTS.md` | Historical narrow passive-stage authorizations; no active G305 permission. |
| `PeripheralBatteryCompanion.sln` | Standalone project plus existing AnyCPU/x64 configurations; build passes. |
| `diagnose-g305-ghub.ps1` | Explicit Windows SQLite diagnostic, bounded/suppressed raw output, clipboard preserved. |
| `docs/G305_HIDPP_FEASIBILITY.md` | Historical stage evidence/commands and limitations; current acceptance is consolidated checklist. |
| `docs/G305_HID_INSPECTOR.md` | Historical stage evidence/commands and limitations; current acceptance is consolidated checklist. |
| `docs/G305_STAGE2_PR_DESCRIPTION.md` | Historical stage evidence/commands and limitations; current acceptance is consolidated checklist. |
| `docs/G305_STAGE2_READONLY_REPORT.md` | Historical stage evidence/commands and limitations; current acceptance is consolidated checklist. |
| `docs/G305_STAGE3A1_PASSIVE_REPORTIDS.md` | Historical stage evidence/commands and limitations; current acceptance is consolidated checklist. |
| `docs/G305_STAGE3A2_IDENTITY_PROTOCOL_REVIEW.md` | Historical stage evidence/commands and limitations; current acceptance is consolidated checklist. |
| `docs/G305_STAGE3A2_REPORT.md` | Historical stage evidence/commands and limitations; current acceptance is consolidated checklist. |
| `docs/G305_STAGE3A3_PRIMARY_SPECS.md` | Historical stage evidence/commands and limitations; current acceptance is consolidated checklist. |
| `docs/G305_STAGE3A3_REPORT.md` | Historical stage evidence/commands and limitations; current acceptance is consolidated checklist. |
| `docs/G305_STAGE3A4_REPORT.md` | Historical stage evidence/commands and limitations; current acceptance is consolidated checklist. |
| `docs/G305_STAGE3A4_ROUTE_EVIDENCE_AUDIT.md` | Historical stage evidence/commands and limitations; current acceptance is consolidated checklist. |
| `docs/G305_STAGE3A_PROTOCOL_REVIEW.md` | Historical stage evidence/commands and limitations; current acceptance is consolidated checklist. |
| `docs/G305_STAGE3A_REPORT.md` | Historical stage evidence/commands and limitations; current acceptance is consolidated checklist. |
| `docs/G305_STAGE3B_EXPERIMENT_PROPOSAL.md` | Historical stage evidence/commands and limitations; current acceptance is consolidated checklist. |
| `docs/STAGE4_OWNER_WINDOWS_TEST_CHECKLIST.md` | Historical stage evidence/commands and limitations; current acceptance is consolidated checklist. |
| `docs/STAGE4_WINDOWS_STABILIZATION_REPORT.md` | Historical stage evidence/commands and limitations; current acceptance is consolidated checklist. |
| `docs/STAGE5_1_OWNER_CHECKLIST.md` | Historical stage evidence/commands and limitations; current acceptance is consolidated checklist. |
| `docs/STAGE5_1_REPORT.md` | Historical stage evidence/commands and limitations; current acceptance is consolidated checklist. |
| `docs/STAGE5_2_OWNER_CHECKLIST.md` | Historical stage evidence/commands and limitations; current acceptance is consolidated checklist. |
| `docs/STAGE5_2_REPORT.md` | Historical stage evidence/commands and limitations; current acceptance is consolidated checklist. |
| `docs/STAGE5_3_OWNER_CHECKLIST.md` | Historical stage evidence/commands and limitations; current acceptance is consolidated checklist. |
| `docs/STAGE5_3_REPORT.md` | Historical stage evidence/commands and limitations; current acceptance is consolidated checklist. |
| `docs/TESTING.md` | Historical/current workflow; A1 stale instructions, unrelated dirty addition excluded. |
| `src/PeripheralBatteryCompanion.Core/BatteryDevice.cs` | Additive precision/connection/mouse state; legacy constructors retained. |
| `src/PeripheralBatteryCompanion.Core/BatteryMonitoringService.cs` | Source-token shutdown guard, independent caches, injected clock; no-overlap preserved. |
| `src/PeripheralBatteryCompanion.Core/GHubGatewayTransport.cs` | Fixed loopback/subprotocol, no proxy or credentials, abort/dispose. |
| `src/PeripheralBatteryCompanion.Core/GHubSqliteBatteryProvider.cs` | Read-only allocation bound/future-time handling; not production live fallback. |
| `src/PeripheralBatteryCompanion.Core/GHubWebSocketBatteryProvider.cs` | GET allowlist, discovery/correlation/bounds/SOC/errors; fake tests, no live calls. |
| `src/PeripheralBatteryCompanion.Core/HyperXAutomaticMonitor.cs` | Initial/manual eligibility and backoff in original single-flight scheduler. |
| `src/PeripheralBatteryCompanion.Core/HyperXStartup.cs` | One readiness-gated initial attempt, Resume interval, durable marker untouched. |
| `src/PeripheralBatteryCompanion.Core/Presentation/BatteryBarGeometry.cs` | Current-width proportional fill, numeric bounds. |
| `src/PeripheralBatteryCompanion.Core/Presentation/BatteryPanelView.cs` | Precise/approximate/unknown and current/stale presentation; no stale fill. |
| `src/PeripheralBatteryCompanion.Core/Presentation/HyperXCompactView.cs` | Neutral secondary copy; explicit technical uncertainty unchanged. |
| `src/PeripheralBatteryCompanion.Core/Presentation/TrayTooltipView.cs` | Fixed bounded labels/63 characters; A2 defensive mismatch noted. |
| `src/PeripheralBatteryCompanion.G305GHubInspector/DatabaseInspection.cs` | Read-only bounded snapshot/schema/latest JSON; no owner DB read. |
| `src/PeripheralBatteryCompanion.G305GHubInspector/JsonInspection.cs` | Structural attribution/redaction/limits; no raw dump. |
| `src/PeripheralBatteryCompanion.G305GHubInspector/PeripheralBatteryCompanion.G305GHubInspector.csproj` | Independent net10.0 console, same Sqlite dependency, no tray/HID reference. |
| `src/PeripheralBatteryCompanion.G305GHubInspector/Program.cs` | Standalone OS/argument/exception privacy gate; G305 opts into cached IDs, HyperX does not. |
| `src/PeripheralBatteryCompanion.G305GHubInspector/packages.lock.json` | New standalone lock uses existing pinned dependency versions/hashes. |
| `src/PeripheralBatteryCompanion.G305Inspector/Program.cs` | Standalone OS/argument/exception privacy gate; G305 opts into cached IDs, HyperX does not. |
| `src/PeripheralBatteryCompanion.HidMetadata/CapabilityInspector.cs` | Exact match before open, anonymous grouping, sanitized numeric failures. |
| `src/PeripheralBatteryCompanion.HidMetadata/InspectionReport.cs` | Optional metadata fields and incomplete outcomes; no raw IDs in formatted reports. |
| `src/PeripheralBatteryCompanion.HidMetadata/PassiveReportIds.cs` | Metadata-only imports, count/ABI checks, bounded ancestry; no reports. |
| `src/PeripheralBatteryCompanion.HidMetadata/ReportFormatter.cs` | Sanitized placeholders/groups/counts, inconclusive ID limits. |
| `src/PeripheralBatteryCompanion.HidMetadata/WindowsHidMetadataBackend.cs` | Zero-access shared-open/native ownership; A3 enumeration cap limitation. |
| `src/PeripheralBatteryCompanion.Windows/BatteryLevelBar.cs` | Validated current fill/precision accessibility; estimate path unchanged. |
| `src/PeripheralBatteryCompanion.Windows/BatteryPopupForm.cs` | Live mouse bindings, compact statuses, neutral copy; tooltip retains uncertainty. |
| `src/PeripheralBatteryCompanion.Windows/TrayApplicationContext.cs` | Gateway at 60s, coordinator Refresh/initial ownership; close waits tasks. |
| `tests/PeripheralBatteryCompanion.Tests/BatteryBarGeometryTests.cs` | Synthetic regression assertions reviewed; no real G HUB/HID required. |
| `tests/PeripheralBatteryCompanion.Tests/G305GHubInspectionTests.cs` | Synthetic regression assertions reviewed; no real G HUB/HID required. |
| `tests/PeripheralBatteryCompanion.Tests/GHubGatewayTests.cs` | Synthetic regression assertions reviewed; no real G HUB/HID required. |
| `tests/PeripheralBatteryCompanion.Tests/HyperXCompactViewTests.cs` | Synthetic regression assertions reviewed; no real G HUB/HID required. |
| `tests/PeripheralBatteryCompanion.Tests/HyperXHelperTests.cs` | Synthetic regression assertions reviewed; no real G HUB/HID required. |
| `tests/PeripheralBatteryCompanion.Tests/HyperXStartupTests.cs` | Synthetic regression assertions reviewed; no real G HUB/HID required. |
| `tests/PeripheralBatteryCompanion.Tests/PassiveReportIdTests.cs` | Synthetic regression assertions reviewed; no real G HUB/HID required. |
| `tests/PeripheralBatteryCompanion.Tests/PeripheralBatteryCompanion.Tests.csproj` | Adds only standalone diagnostic project reference; no dependency upgrades. |
| `tests/PeripheralBatteryCompanion.Tests/TrayPresentationTests.cs` | Synthetic regression assertions reviewed; no real G HUB/HID required. |
| `tests/PeripheralBatteryCompanion.Tests/WindowsStabilizationTests.cs` | Synthetic regression assertions reviewed; no real G HUB/HID required. |
| `tests/PeripheralBatteryCompanion.Tests/packages.lock.json` | Existing dependency entries/hashes unchanged; new project reference only. |
| `tests/PeripheralBatteryCompanion.Windows.Tests/PopupLayoutTests.cs` | 37 native/STA cases including new labels/paint/fit; compiled only, owner execution pending. |

## Final local checks and GitHub limitation

Local publish/static package gate **PASS**: self-contained Windows helper included, native/diagnostic projects isolated from tray graph, no private DB/capture files. Runner harness **15/15 PASS**. No local packaged executable/helper was launched. Audit adds **zero source/test changes** and zero new tests; the 508/37/545 figures remain unchanged. Final diff adds this report, current checklist and prepared description only (14 total commits / 66 changed files versus main after the audit commit, provided no concurrent changes).

`gh pr view 7`, REST `gh api .../pulls/7`, and `gh run list` failed with Forbidden. Authorized `gh pr edit 7 --body-file docs/PR7_DESCRIPTION.md` and `gh pr ready 7 --undo` also failed with GraphQL Forbidden. Thus the remote description update and draft confirmation could not be completed; no success is claimed. This is GitHub API access failure, not an automatic approval-review rejection. No credential workaround, extra PR, merge or release attempted.

Owner can paste the prepared body into PR #7 and verify/retain Draft directly in GitHub (or run the same gh commands with authorized access). CI results/reviews/mergeability remain **unavailable**, not green. The owner-reported initial Draft state was never intentionally promoted to ready. Final audit commit SHA is supplied in delivery / `git log -1`; runtime source is still exactly reviewed 67030bf. Stop for owner Windows validation and explicit merge approval.
