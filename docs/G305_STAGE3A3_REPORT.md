# G305 Stage 3A.3 report

2026-10-28. **Partial review delivered; full primary PDF review blocked by access. Stage 3B NO-GO.** Do not describe this stage as a completed review of either PDF. No hardware I/O, source/test change, new decoder, executable packet, merge, release or branch deletion.

## Branch and files

Branch `feature/g305-stage3a3-primary-specification-review` starts at exact Stage 3A.2 `20b0041`, preserving 12da59c, 51e2821, 1af72a0 and 69b607a. No cherry-pick or duplicated implementation. Stage commit is reported at delivery and listed by `git log --oneline 20b0041..HEAD`.

Changed files:

- G305_STAGE3A3_PRIMARY_SPECS.md: access ledger, pinned readable manufacturer sources, owner-supplied excerpt provenance, unresolved routing/notification/error/battery semantics, field review and no-go.
- G305_STAGE3B_EXPERIMENT_PROPOSAL.md: current source limits, C52B scope warning and next non-transmitting evidence step.
- This report.

## Research and limitations

Direct legacy/draft PDF requests and www-host equivalents failed with tunnel HTTP 403 Forbidden. No indexed-web/PDF tool exists in this session; search-engine access and GitHub API listing were also unavailable. Public manufacturer cpg-docs git clone succeeded at f9107a4e3190d798f9be8c5d596f255f5e5741d2: read root README, HID++ 2.0 README and IRoot. No PDF or detailed battery definition exists in that tree. Manufacturer-linked public Drive folder also returned 403. No third-party implementation or proprietary binary consulted, no insecure workaround attempted.

Exact URLs/section citations and the distinction between independently read sources and owner's reported PDF sections are in the review. Full connection notification grammar, legacy/draft errors and 0x1000 function/output definition remain **unreviewed and outstanding**. General root function/success fields are documented, but mouse index, applicability, ownership, reserved values and exact dual-family decoder remain unresolved. No percentage fabricated. P1/descriptor IDs do not identify a live mouse.

Smallest next step: owner supplies the two public PDFs as attachments or an accessible authoritative copy for offline review. No active or additional passive hardware query required. Implementation and physical execution each require separate later approval.

## Checks and delivery

Baseline recorded from Stage 3A.1: Release build passed with zero warnings/errors; 126 focused tests passed, zero failed/skipped. These results were **not rerun** here. Markdown-only stage: no new tests; Release build/full suite/Windows/clipboard/hardware tests **NOT RUN**. Documentation whitespace/local file links, exact-base ancestry and documentation-only path checks performed before push. All source, tests, launchers, G915/HyperX, tray/dashboard/startup and packaging remain byte-identical to 20b0041.

[Compare with main](https://github.com/Jau1Agiota/G-Hub-battery-tray-icon-and-smartphone-app-/compare/main...feature/g305-stage3a3-primary-specification-review?expand=1). Includes preserved unmerged prior G305 stages plus this documentation stage. No PR creation is required by this briefing; compare link is the delivery path. Stop for owner review; no Stage 3B authorization inferred.
