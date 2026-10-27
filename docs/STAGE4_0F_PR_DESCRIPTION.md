# Stage 4.0F: supervised HyperX manual battery transport

The tray's HyperX query was blocked because the standalone POC's pending native I/O could outlive cancellation. This draft adds a production-owned self-contained helper with atomic Windows Job containment and bounded private IPC, so active report buffers stay outside the long-running tray.

The button becomes functional only after session acknowledgement, exact receiver metadata and a live query-free normal/forced-exit preflight. Default/startup/Refresh/reconnect behavior still sends zero battery requests. A subsequent manual click permits at most one verified 62-byte 66/89 request and one pre-armed input, with a three-second native exchange deadline. NGENUITY must be observed running by parent/helper; unknown/closed state refuses this concurrent mode. The separate closed-app POC keeps its existing safeguards.

## Safety and uncertainty

- Suspended CreateProcess assigns a non-inheritable kill-on-close Job atomically via JOB_LIST and restricts inherited handles. No uncontained execution fallback; no shell/diagnostics launcher invocation.
- A validated candidate is accepted only after normal helper exit 0. Forced/crashed/unconfirmed exit cannot promote a value. Cancel is cooperative first, then explicit bounded termination/exit observation. Unconfirmed ownership is retained and further attempts stop; no pending HID memory is freed by the parent.
- Global cooperating single-flight, no retries/polls/initializers/feature commands/queue drain or permission fallback. Candidate (Ambiguous), source/time, stale/receiver/link distinctions remain; never ConfirmedFresh.
- POC native code is shared in a separate library instead of duplicated. An existing cancellation-ordering race was corrected with a separate owned/drained I/O token; its original failing assertion remains.
- A defective driver can delay final kernel reclamation. That outcome is never called clean or successful. The isolation/OS contract is documented; actual HID cancellation and repeated coexistence still require separately approved validation.

## Verification

- Locked restore and Release solution build passed, 0 warnings/errors.
- 265 portable tests passed (229 existing + 36 new); 15 runner-contract checks passed.
- Ten Windows layout/process/ABI tests compiled but were not run in Linux. Process tests explicitly prohibit NativeOneShot and use only no-HID simulations. Remote CI outcome not yet observed.
- Release publish and static artifact gate passed: full self-contained helper/runtime included; active native/POC assembly absent from the tray dependency graph; no diagnostic executable/tray/SQLite dependency in the helper. Final ZIP is rebuilt from the committed tree.
- G915 reader/monitor/icon, verification/diagnostic launchers, tray placement and existing NuGet versions/hashes preserved. No hardware query executed during development/tests.

See [native lifetime/IPC ADR and one-test approval gate](docs/HYPERX_NATIVE_TRANSPORT.md) and [implementation report](docs/STAGE4_0F_REPORT.md). Prior one-shot NGENUITY success is consistency evidence, not unique response ownership or continuous reliability.

Draft targeting main from feature/stage4-0f-hyperx-native-transport (base 4dbf1a3, which already contains integration PR #3). Review Windows no-HID CI and code before granting one physical test. No merge, branch deletion, additional hardware query or automatic polling is authorized. Prefer squash merge after final review.
