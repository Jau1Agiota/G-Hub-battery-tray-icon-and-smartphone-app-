# Development and regression policy

Preserve the existing project identity, independent G HUB findings, and working reader/monitoring behavior. Do not consult or copy other G HUB battery-monitoring implementations. Keep runtime dependencies minimal. Never add real G HUB databases, device identifiers, credentials, or user-specific paths.

For every stage, inspect existing tests and establish a baseline from recorded results or one necessary run before changes. Add appropriate regression tests and preserve existing assertions. For a reproducible bug, add a failing test when practical, implement the smallest correction, and verify that test. If automation is impractical, document a repeatable manual check.

The owner prefers to run complete builds and regression suites locally with `test.ps1` and share its clipboard summary. Perform only the focused syntax, compilation, or smoke checks necessary to deliver usable changes; do not repeatedly run the full suite without a new failure or change that justifies it. Full local regression and native desktop/device checks remain required before declaring a stage fully validated. Use `docs/TESTING.md` for commands and the manual checklist. Never describe pure state tests or mocked interactions as proof of native Windows UI behavior.

Keep `test.ps1` and the lightweight Windows CI consistent. Do not weaken tests, suppress verification, fabricate counts, or discard failed/skipped/not-run distinctions. New logging must not collect application database contents or secrets. Original-code licensing still awaits owner approval.

Each cloud task is isolated; use the existing checkout without creating a worktree unless requested. Work on a dedicated feature branch, preserve user changes, and never merge the default branch automatically. Stage 4.0A is explicitly authorized for passive G305 HID metadata inspection only. Do not send HID reports or implement a production G305 battery provider without separate authorization and verified findings. Do not infer that prior Windows/UI fixes were locally validated without owner confirmation.
