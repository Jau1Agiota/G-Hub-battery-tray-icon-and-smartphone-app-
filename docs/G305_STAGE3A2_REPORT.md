# G305 Stage 3A.2 report

2026-10-28. Result: documentation-only identity/protocol research, **Stage 3B NO-GO**. No executable packet or new code. No hardware queries, report capture, merge, branch deletion or release.

## Base and changes

Branch: `feature/g305-stage3a2-identity-protocol-research`, created from exact `12da59c59f993d304aa0438d3b38dbd866222ef5`. Verified ancestry preserves Stage 2 commits `69b607a`, `1af72a0`, Stage 3A `51e2821`, and Stage 3A.1 `12da59c`. Actual remote default remains main at `2948558cd2f39c4fde8e2b9fe3aab31c318d5c3a`. No cherry-pick/merge or rewriting prior work.

Files changed:

- G305_STAGE3A2_IDENTITY_PROTOCOL_REVIEW.md: owner-observed IDs/parent grouping, decision table, sourced receiver/mouse distinction, incomplete field design, coexistence and next step.
- G305_STAGE3B_EXPERIMENT_PROPOSAL.md: updated ReportID/evidence matrix, removed obsolete pending-result/minimum-diagnostic instructions; preserved no-go and separate approvals.
- This report: provenance, delivery, validation and blockers.

Only Markdown changed relative to 12da59c. All existing source, tests, launchers, production G915/HyperX, dashboard/tray/startup and packaging remain byte-identical to that base.

## Sources and blockers

Reviewed Logitech cpg-docs HID++ 2.0 README Packet Structure tables 1–5 and IRoot GetProtocolVersion/GetFeature tables. Exact URLs, sections and retrieved-content SHA-256 hashes are in the identity review. Both historical manufacturer PDFs hosted at lekensteyn.nl failed retrieval with tunnel HTTP 403 Forbidden; no claim to have read them. No third-party battery-monitor code or proprietary binaries consulted.

Owner passive results confirm COL01 0x10/7 and COL02 0x11/20 button capability IDs; COL03 0x20/0x21 remains unexplained/excluded. P1 identifies cached USB ancestry only. No mouse slot, live physical identity, protocol version or battery value is established. A general receiver address does not make a receiver query a mouse-identity query. Software ID policy, reserved parameters, applicability and complete legacy error grammar remain unresolved. No speculative packet emitted.

Recommendation: obtain accessible public manufacturer routing/error specification copies for offline review, then assess C53F applicability. Do not rerun confirmed passive diagnostics or sample live reports to compensate. Owner approval for implementation and separately for physical execution is still required after a complete proposal is reviewable.

## Validation and delivery

Recorded baseline: Stage 3A.1 Release solution build passed, zero warnings/errors; 126 focused tests passed, zero failed/skipped. Those are previous recorded results, **not rerun in Stage 3A.2**. No source/test change justifies recompilation under repository minimum-check policy. New tests: none (documentation-only). Release build, full suite, Windows/device/clipboard tests: **NOT RUN** in this stage. Owner's reported Windows passive observations are explicitly distinguished from cloud verification.

Stage checks: clean starting tree, exact base/ancestry verified, documentation-only path audit, local Markdown link check and `git diff --check`. No native API executed. Stage 3A.2 commit is listed by `git log --oneline 12da59c..feature/g305-stage3a2-identity-protocol-research`; final delivery response supplies its hash.

[Compare / create PR](https://github.com/Jau1Agiota/G-Hub-battery-tray-icon-and-smartphone-app-/compare/main...feature/g305-stage3a2-identity-protocol-research?expand=1). Comparison with main includes preserved unmerged Stages 2/3A/3A.1 as well as this documentation-only stage. If PR creation is Forbidden, use this link; do not bypass access controls. No automatic merge.
