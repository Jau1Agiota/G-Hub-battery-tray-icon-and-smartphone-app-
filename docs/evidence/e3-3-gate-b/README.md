# E3.3 scoped Gate B evidence

Final local session: `20261101T132922366Z`, based on `8b31236` plus the dedicated
Gate B source/runner changes. These files are sanitized exports, not raw OS traces.
`<REPO>` replaces the local checkout prefix. Original TRX/VSTest logs remain in
the gitignored `test-results/e3-3-gate-b/20261101T132922366Z` directory.

- `allowlist.txt`: exact positive method filter (no native, digest or token-writing tests).
- `executed-cases.json`: 15 actual TRX case names, outcomes and durations.
- `*.stdout.log`: baseline/probe discovery and allowlisted test output.
- `*.process.json`: command arguments, PID/parent chain, process start/exit times,
  event-access limitation, and final remaining-testhost snapshots.
- `copy-integrity.json`: byte comparisons for both 86-file copies; only probe JSON differs.
- `dependency-closure.json`: matching identities/assets hash strings for 13 packages;
  this is not a fresh archive or loaded-runtime attestation.
- `summary.json`: counts and relative evidence location; 8 vs 15 discovery cases,
  15 passed, zero failed/skipped.

OS event tracing was denied. Process records use CIM polling plus retained process
handles, not complete event tracing. No test DLL was loaded into the monitor.
JSON consumption is shown by changed theory enumeration with identical binaries;
it does not prove parallelism-key precedence or constitute a file-open trace.
The earlier two successful focused runs are not additive unique-test counts.

Gate E remains BLOCKED and UNAUTHORIZED. Owner Gate B acceptance is pending.
