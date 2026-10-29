# G305 Stage 3A.3 completed review report

2026-10-28. **Both supplied PDFs read in full; research access blocker resolved. Stage 3B remains NO-GO.** The initial partial report at e065251 is superseded by this completed review. Documentation only; no source/test change, sender, executable packet, hardware access, merge, release or branch deletion.

## Branch and preservation

Continued on `feature/g305-stage3a3-primary-specification-review` from exact `e065251`, retaining Stage 3A.2 20b0041, Stage 3A.1 12da59c, Stage 3A 51e2821 and Stage 2 1af72a0/69b607a. No history rewrite, branch replacement or duplicated implementation. Completion commit is supplied in delivery and listed by `git log --oneline e065251..HEAD`.

Changed files: G305_STAGE3A3_PRIMARY_SPECS.md, G305_STAGE3A3_REPORT.md and G305_STAGE3B_EXPERIMENT_PROPOSAL.md. Previous stage documents remain historical records; this review/proposal states the current evidence. All production/source/test/launcher/packaging content remains byte-identical to e065251.

## Sources and method

Owner attachments Logitech_HIDPP_1.0.pdf (19 pages, SHA-256 `24aca57c431863fbfdba9a2b838a5a4dad59deb90ebe5718a675cdc0bf54642c`) and Logitech_HIDPP_2.0.pdf (16 pages, June 4 2012 draft, SHA-256 `c3d642248df91ee2d00420c359e4693ea36384b63d308abbc45bb4838919e58b`) were extracted locally and read. Version and battery-status pages were rendered/visually checked to verify contradictory text. URLs and physical PDF page/section citations appear in the primary review. Public cpg-docs README/IRoot pinned at f9107a4e3190d798f9be8c5d596f255f5e5741d2 provides the earlier cross-check; no third-party monitor code/proprietary binaries used. No PDF, extracted proprietary data or runtime dependency added to git. Public-host access failures remain historical, not a claim that attachment review was blocked.

## Findings

- C52B-scoped legacy routing assigns wireless indices at receiver level; neither passive report IDs nor P1 yields a C53F mouse index. Notification fields and enablement requirements are now documented, with no notification configuration/listening authorized.
- Version request general fields include two explicit zero parameters, root/function and ping; success major/minor/echo and nonzero software-ID rules are established at specification level, not verified on this G305.
- Legacy error envelope/code meanings are documented. The draft has a version error echoed-address discrepancy and conflicting ping prose; general 2.0 error codes are given without a complete wire envelope. No silent repair or speculative decoder.
- Responses can differ in report size from requests; draft example returns a long version response to a short request. C53F cross-collection routing/receive ownership is unverified.
- Battery 0x1000 functions 0 (status), 1 (capability), event 0, status fields and level capability mapping are now documented. 0 means unknown; 25%/50% can encode low/good categories. Actual G305 support/runtime index/precision remains unknown. Draft status enum/validation conflict and incomplete capability layout are retained as limitations.

No exact packet or hardware-test proposal emitted. Stop for owner review. Remaining questions concern C53F-specific route/identity/applicability, draft clarification/general error envelope, cross-collection response path, G HUB correlation and bounded transport cleanup. Explicit later implementation approval and separate physical execution approval are still required.

## Validation

Documentation whitespace, local Markdown file-link check, preserved-ancestry and documentation-only path audit passed. Attachment SHA-256 and physical page counts recorded; selected original diagrams visually checked. **Build/tests not rerun** for Markdown-only changes. Last recorded Stage 3A.1 baseline is Release build zero warnings/errors and 126 focused tests passed, zero failed/skipped; those are not new results. Windows/clipboard/hardware validation **NOT RUN**; owner-passive findings are attributed as such.

[Compare with main](https://github.com/Jau1Agiota/G-Hub-battery-tray-icon-and-smartphone-app-/compare/main...feature/g305-stage3a3-primary-specification-review?expand=1). Includes preserved prior unmerged G305 stages. No PR/merge/release action required for this completion; branch push is the delivery.
