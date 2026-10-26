# Local verification and regression policy

Stage 3.1 separates the information popup from application lifecycle controls. Complete local regression and real Windows interaction checks are required before this stage is fully validated. The developer's Linux compilation/focused tests are not a substitute for these checks.

## Run verification

Use Windows PowerShell 5.1 or PowerShell 7 on the Windows development machine, with the .NET SDK selected by `global.json` available on PATH. No administrator privileges, additional PowerShell modules, hardware, or G HUB installation are required for automated tests. Open an ordinary PowerShell terminal in the checkout and run:

```powershell
.\test.ps1
.\test.ps1 -Release
.\test.ps1 -BuildOnly
.\test.ps1 -Verbose
.\test.ps1 -CopySummary
.\test.ps1 -Release -CopySummary
```

For the owner handoff, use **`.\test.ps1 -Release -CopySummary`**, then paste the clipboard summary into the conversation. Running Debug as well is useful before debugging in Visual Studio. To call from another directory, use the call operator and a quoted script path, for example `& 'C:\source folder\PeripheralBatteryCompanion\test.ps1' -CopySummary`. This example is a generic checkout location, not an application setting.

| Option | Effect |
| --- | --- |
| No switches | Locked restore, Debug build of solution/application and tests, all discovered automated test projects, concise summary |
| `-Release` | Same workflow in Release |
| `-BuildOnly` | Restore and build; tests explicitly NOT RUN |
| `-Verbose` | Stream normal build output and detailed test console diagnostics, also saved in the log |
| `-CopySummary` | Copy actual statuses, counts and useful failure details using `Set-Clipboard` |

Switches combine. `-BuildOnly -Release -CopySummary` builds Release and copies a summary clearly stating that tests were not executed. The runner does not launch the tray app, install software, change execution policy/Windows settings, terminate G HUB, inspect credentials, or access its database.

The solution and projects are resolved relative to the script, not the current directory. The conventional solution is preferred; if it is absent, one alternative solution or the discovered C# projects are used. Test projects are discovered from `IsTestProject` / `Microsoft.NET.Test.Sdk`, not a fixed test count. A missing required SDK, missing projects, restore/build failure, missing/invalid current results, all-skipped/zero-executed test project, or failed test causes failure. SDK/restore/build/test command failures retain their original native exit code; runner/result/clipboard errors use a nonzero runner code. A failing restore stops the build/tests; a failing build stops tests. Other test projects still run after a test failure so their results can be reported.

From an interactive shell, inspect `$LASTEXITCODE` immediately after the script. A build-only PASS proves compilation, not regression coverage. A skipped test is never counted as passed. If a requested clipboard copy fails, verification exits nonzero and the saved summary identifies that separately; the test results remain available in the files below.

## Output and logging

The concise output has this shape; values and counts are calculated from this run's exit statuses and TRX files:

```text
Peripheral Battery Companion
Local Verification
.NET SDK: <selected SDK>
Configuration: Release
Restore:           PASS / FAIL / NOT RUN
Build:             PASS / FAIL / NOT RUN
Automated tests:   PASS / FAIL / NOT RUN
Passed: <actual count>
Failed: <actual count>
Skipped: <actual count>
Executed: <actual count>
Result files read: <parsed> / <expected>
Clipboard: PASS / FAIL / Not requested
Overall result: PASS / FAIL (exit <code>)
Native tray / physical-device checks: NOT RUN by this script
```

When no current results are readable, counts say unavailable rather than claiming zero passing/failing tests. Failed includes failed tests and other non-passing, non-skipped outcomes such as aborted or inconclusive results. Failure summaries include the failing command and exit code, useful error output, and test names/assertion messages/stack traces when present. Multi-project counts can be partial; `Result files read` and per-project errors identify that limitation.

The gitignored `test-results/` directory contains:

- `latest.log`: readable sanitized command output and summary, overwritten per run.
- `summary.txt`: concise clipboard-ready result, overwritten per run.
- `automated/<project number>/results.trx`: current machine-readable results.
- `runner.lock`: an empty coordination file to prevent concurrent runs from corrupting the current results.

The runner owns `test-results/automated/` and recreates it before every run, so old passing files cannot mask missing results and log accumulation is bounded. Do not store personal files in this generated directory. Repository/profile path prefixes and obvious credentials in URL user-info/query parameters are redacted from readable output. TRX files are unmodified diagnostic artifacts and can include normal local source/SDK paths; review them before sharing. No raw G HUB data is logged. Sanitization is a convenience, not a reason to include secrets in tests or exception messages.

## Automated coverage and its boundaries

Existing reader/monitoring tests remain unchanged: verified primary JSON value, warning disagreement, malformed/missing/empty payloads, ranges/types, UTC parsing, missing/corrupt/locked/incompatible SQLite databases, live synthetic WAL reads, unchanged timestamps/values, cached failures, cancellation, polling and shutdown waiting for in-flight work.

Stage 3.1 adds pure state/presentation/geometry tests for:

- Left-click toggling, repeated/idempotent open, dismissal without exit, right-menu transition, and irreversible explicit exit state.
- Percentage changes, invalid/unavailable states, cached labels, unknown/unsupported G305, and shared color thresholds.
- Popup placement for four taskbar edges, auto-hidden taskbar fallback, negative monitor coordinates, and scaled sizes.
- Refresh updating the presentation while its visibility state is hidden, including recovery from unavailable data.

These tests **do not simulate real Windows tray events**, count native windows, verify the Windows clipboard, or prove icon/resource disposal or focus ordering. One popup is constructed and retained by the context; closing/hiding it does not dispose the context. The monitor's existing shutdown tests cover the asynchronous lifetime logic. The native adapter/resource lifetime requires the manual checklist.

## Test-runner contract checks

The runner has a small, framework-free smoke harness:

```powershell
.\scripts\Test-VerificationRunner.ps1
```

It invokes copies of `test.ps1` in synthetic temporary checkouts with spaces in their paths, from another working directory. A fake dotnet command covers success/skip counts, Release and combined switches, BuildOnly, verbosity, missing SDK/project, project fallback, restore/build/test exit propagation, clipboard summaries containing failures, zero tests, stale/missing/malformed results, and clipboard failure. No actual .NET build or real clipboard operation occurs in this harness. Fixtures are removed afterwards. CI runs it before the real `test.ps1 -Release` workflow. Native `Set-Clipboard` behavior still needs the owner's ordinary `-CopySummary` run.

## Manual Windows integration checklist

Stop older copies, build/run the Windows startup project in Visual Studio (Debug / x64), and use a normal Windows user account. Do not rename/delete/edit the real G HUB database or its WAL/SHM files for testing.

1. **Left-click:** open the tray popup. Check G915 name, actual reader-provided percentage, horizontal indicator, and last-reported time. No Exit Application command, database-access detail, or unverified connectivity claim appears in this popup. G305 says Battery Unknown and monitoring not yet supported.
2. **Toggle and rapid clicks:** click the tray icon again; the popup hides and the process/icon remain. Repeat slowly, rapidly, as a double-click, and with the second mouse press held briefly. Reopening must show one popup and current information. This specifically checks shell activation/deactivation ordering; pure state tests cannot prove it.
3. **Dismissal:** reopen and independently test Escape, Close, the caption X, and clicking another application/desktop. Each hides the popup without exiting. Check both mouse and Alt+Tab focus changes. Deactivation dismissal waits until mouse release to avoid reopening on a second tray click; it must still feel predictable on the actual taskbar.
4. **Right-click:** with the popup hidden, open the application menu. It must contain only Open Battery Panel, Refresh, and Exit Application. With the popup visible, right-click again: the popup hides and one context menu opens. It must not toggle the left-click behavior or leave duplicate panels. Open Battery Panel reopens it after the menu closes.
5. **Refresh and hidden monitoring:** choose Refresh in the popup and in the context menu. The UI remains responsive. Hide the popup for at least 65 seconds, then reopen: monitoring still runs. Existing SQLite/database-access timestamps remain internal even though the everyday popup no longer shows all diagnostics.
6. **Real G915:** while G HUB is running, compare the displayed primary percentage with G HUB's UI. An unchanged/older record time is allowed. Turning off the keyboard must not cause a fabricated connectivity claim. The popup may show the stored last report. Never substitute the warning percentage.
7. **Unavailable data:** use the existing synthetic tests for bad/locked data. Optionally run the app under a separate test account without a G HUB database: gray/unavailable state, responsive Refresh and Close, and no database creation. After access failure, any cached percentage must be labeled cached, with a gray current indicator.
8. **Position and DPI:** test primary/secondary monitors (including a monitor left of/above the primary), tray overflow, normal display resolutions, and Windows scaling such as 100%, 150%, and 200%. The popup stays in the selected monitor's working area; labels/buttons fit or remain reachable by scrolling. Test top/left/right taskbars only if the Windows/taskbar configuration supports them; mark unavailable configurations NOT RUN. Moving/changing display settings must not cause repeated activation or offscreen windows.
9. **Lifetime/resources:** open/close many times, then use Exit Application, including during Refresh. Monitoring cancels, the process exits, and the tray icon disappears. Relaunch successfully (mutex released). Confirm hiding/closing the popup never creates another process or stops monitoring. With Visual Studio diagnostics/Task Manager, check repeated popup cycles do not leave growing window/icon/GDI handle counts.
10. **Runner and clipboard:** run `.\test.ps1 -Release -CopySummary`, confirm clipboard contents match `test-results/summary.txt`, then share that summary plus checked/failed/NOT RUN desktop items. `-BuildOnly` must explicitly report tests NOT RUN. Do not describe an unobserved Actions run as passed.

## Permanent regression workflow

Before each stage, inspect affected tests and record an existing baseline or one necessary baseline run, including pre-existing failures. Keep changes focused. For a reproduced bug, add a failing regression test when practical, implement the smallest fix, and verify it. If automation is impractical, document a repeatable desktop/device check instead.

The developer performs focused checks as necessary; the owner runs the full local workflow and shares the copied summary. Fix reported failures, then the owner reruns the affected/full verification. Do not weaken assertions or hide failures to obtain a pass. Every future stage must preserve existing behavior and add appropriate tests. Stage 4 remains blocked until the owner validates and approves Stage 3.1.
