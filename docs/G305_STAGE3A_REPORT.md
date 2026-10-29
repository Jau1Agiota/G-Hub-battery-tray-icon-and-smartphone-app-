# G305 Stage 3A implementation report

2026-10-28. Documentation-only protocol preparation; Stage 3B **NO-GO**. No hardware access, packet construction, native imports, report parser, sender, provider or tray behavior was added.

## Branch and preserved work

Actual remote main verified at `2948558cd2f39c4fde8e2b9fe3aab31c318d5c3a`; it is an ancestor of the integrated base. Stage 2 remains unmerged. New branch **feature/g305-stage3a-hidpp-protocol-prep** starts from Stage 2 `1af72a01389aa778cf1cd270bdb0424a8cca8e68`, preserving `69b607a` and `1af72a0` without duplicate/cherry-picked implementations. No merges, deletions or releases performed. Stage 3A commit(s) are listed by `git log --oneline 1af72a0..feature/g305-stage3a-hidpp-protocol-prep`.

Compare / PR creation link:
https://github.com/Jau1Agiota/G-Hub-battery-tray-icon-and-smartphone-app-/compare/main...feature/g305-stage3a-hidpp-protocol-prep?expand=1

The comparison includes the two preserved Stage 2 commits while those remain unmerged. PR description should distinguish inherited Stage 2 implementation from this documentation-only Stage 3A. Preferred review wording: “G305 read-only investigation and passive HID++ protocol preparation; no active I/O.”

## Changes and findings

- G305_STAGE3A_PROTOCOL_REVIEW.md: owner evidence, Stage 2 result, source provenance, HID report-size conventions, passive descriptor limits, protocol-family/battery distinctions and identity/coexistence risks.
- G305_STAGE3B_EXPERIMENT_PROPOSAL.md: unresolved field table, one-purpose/one-request design, three-second proposed deadline, shared access, ambiguity/errors, bounded cleanup and separate implementation/execution approvals.
- G305_HIDPP_FEASIBILITY.md: follow-up link; original findings preserved.
- This report: branch/base, validation, limitations and handoff.

The two requested historical PDFs could not be retrieved; their contents were not claimed as reviewed. Logitech public README/IRoot and Microsoft caps docs were reviewed. No third-party battery implementation or proprietary binary consulted. A 0x1000 feature name does not establish a supported battery function/index or decoder. No G305 percentage established.

## Validation and limitations

Baseline: Stage 2 recorded 39 focused tests passed and a Release solution build with zero warnings/errors. For this documentation-only change, ran the existing Release test binaries using `dotnet test tests/PeripheralBatteryCompanion.Tests/PeripheralBatteryCompanion.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~HidInspectorTests|FullyQualifiedName~G305GHubInspectionTests'`. **72 passed, 0 failed, 0 skipped** (39 SQLite/JSON and 33 passive HID cases). Includes exact G915 exclusion, wrong interface filtering, sanitized errors and the metadata-only native-import allowlist. These are synthetic tests, not physical device evidence. No new code/helpers means no new unit tests needed.

Release compilation **not rerun**: only Markdown changed; previous recorded build remains the baseline, not a newly verified build. Full suite, Windows/device access, G HUB coexistence and physical tests **NOT RUN**. `git diff --check` and documentation-only path audit passed. All Stage 2 code, existing tests, production providers, G915/HyperX/native helper, dashboard/startup/scheduler, packaging and test.ps1 remain byte-identical to the Stage 2 base.

## Owner handoff

Review the unresolved fields before authorizing anything active. No hardware command is provided. Smallest useful further hardware-related work would be a separately approved zero-access cached-capability ReportID metadata extension, described in the proposal; it cannot resolve wireless identity/version/battery support alone. Accessible primary legacy/draft specifications would help resolve general protocol semantics. Stage 3B implementation and a later single physical attempt require separate explicit approvals. No production integration or polling is proposed.
