# Stage 4.0D implementation report

Branch: `feature/stage4-0c-hyperx-battery-poc`, baseline `a9a2a33`. Commit is recorded in Git history/delivery. No merge or production integration.

## Change and baseline

Owner evidence: Stage 4.0C's exact wire request/response and 56% NGENUITY comparison are recorded in the [USBPcap record](HYPERX_USBPCAP_VERIFICATION.md). The subsequent passive coexistence test succeeded with NGENUITY open (SharedOpenSucceeded, no Win32 error, shared GENERIC_READ/overlapped, no report I/O). Previous docs still said coexistence was wholly pending and no active mode existed; those statements are now updated to distinguish successful passive access from still-unexecuted active coexistence.

Added `ActiveCoexistence` with separate `AcknowledgeCoexistenceRisk` (equivalent explicit CLI pair). Invalid/mixed modes or missing acknowledgements are rejected before device access. Normal ReadBattery still requires closure acknowledgement and refuses NGENUITY. The new mode requires an observed NGENUITY process; failed process checks or no observation fail closed. No process is stopped or renamed. Both active modes share the same existing per-session semaphore.

The new mode reuses the existing exact target/capability gates, shared READ access, verified fixed 62-byte 66/89 request, at most one output and one pre-armed input, and three-second exchange deadline. No retries, alternate commands, queue drains, additional report reads or timeout overrides. Native I/O/cancellation implementation and pinned-buffer ownership are unchanged. An optional validated-open callback emits only capability metadata after actual-handle validation, without exposing interface paths.

Sanitized output includes mode, numeric metadata/access/share flags, process observation, operation/error results, active invocation elapsed time and bounded validated reply. Valid concurrent candidates display CandidateObservedAmbiguous, with response ownership always ambiguous. Exit 0 is observation success only; no uniquely correlated result is claimed. No additional byte invariants were inferred. Unexpected errors retain uncertain delivery. Ordinary completion/disposal follows the existing SafeHandle/IOCP policy; cancellation cannot force a broken driver to complete, so resource reclamation may defer to standalone exit. This remains unsuitable as an unreviewed long-running provider.

No production provider/Core/Windows/tray/Android change, no original inspector source change, no new native import or package dependency. Existing passive mode and test.ps1 remain intact. Updated preview wording reflects the owner's now-verified full payload/transport and distinguishes normal closure from explicit coexistence.

## Verification

- Release solution build: passed, 0 warnings, 0 errors.
- Focused Release tests: **61 passed, 0 failed, 0 skipped** (46 prior POC/passive cases + 15 new coexistence cases).
- Existing coverage preserved: exact collection/capability filtering, missing/ambiguous receiver, malformed/short/zero/unexpected/out-of-range replies, read/output errors, pre-buffered refusal, deadline/cancellation, one send/read and disposal, exact payload/import whitelist.
- New tests: opt-in/mixed-mode rejection, normal versus coexistence process policy, ambiguous candidate output, shared-open errors 5/32, malformed concurrent replies, timeout/cancellation and one-shot/disposal behavior.
- PowerShell 7 parser: passed. Ten launcher mode/platform checks passed on Linux; accepted combinations stopped at the Windows guard, rejected combinations stopped at argument validation. These checks performed no native I/O. Windows PowerShell 5.1 syntax compatibility is preserved, but actual execution on 5.1 remains owner verification.
- Full portable suite was not rerun; three Windows Forms tests and actual driver/IOCP/concurrent input behavior were not exercised. No physical active coexistence invocation was performed, and no hardware-validation claim is made for Stage 4.0D.

## Owner handoff

Review [coexistence risks, exact command, interpretation and rollback](HYPERX_NGENUITY_COEXISTENCE.md) first. Physical execution is deferred. After owner review, one approved invocation may be made with NGENUITY/helper operating normally; avoid manual refresh. Stop on audio/NGENUITY disruption, reset or unexpected activity; no automatic retry or permission fallback. Paste sanitized result and observations, then wait. A matching percentage is consistency, not proof of unique correlation. Further active trials/capture repeats require fresh approval; production integration requires separate reliability testing and design review.

## Subsequent owner result and consolidated integration

The Stage 4.0E briefing reports one successful physical active coexistence trial on 8ca502e: 56% candidate, exit 0, 77.5 ms, NGENUITY/audio nominal afterward. This supersedes the implementation-time pending-hardware state for that one observation; response ownership remains ambiguous. No new Codex-run hardware test occurred. The [final integration](HYPERX_INTEGRATION.md) adds production metadata/state/UI with active native queries still blocked by the lifetime gate, not automatic polling.
