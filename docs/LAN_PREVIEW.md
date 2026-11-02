# LAN preview

Development transport: **LAN preview · unencrypted and unauthenticated.** Any
reachable peer can read snapshots. This does not replace Gate E. Existing security
holds, providers, monitoring cadence and authenticated transport are unchanged.

## Configure Windows

Right-click the tray icon, choose LAN preview, explicitly select the assigned home
LAN IPv4 address and port (default 8765), then Enable for this session. Ordinary
startup is OFF. Stop in that dialog or Exit Application. A local command-line
alternative is `PeripheralBatteryCompanion.exe --lan-preview <PC_PRIVATE_IPV4> 8765`.
No settings automatically enable the listener on the next launch.

Only GET /v1/battery-snapshot is served, bound to that assigned RFC1918 IPv4 address.
No wildcard, IPv6 or loopback listener is created. Requests read immutable existing
v1 publications through CachedSnapshotPublisher; they never refresh battery sources.
G305 approximate values and HyperX ESTIMATE/AMBIGUOUS attribution remain explicit.
Unknown/current-null and stale lastKnown retain their meanings.

The TCP listener uses 8-KiB headers, three-second connection deadlines, one
connection at a time, 120 matching-route requests/minute, no bodies/keepalive,
no-store responses and the existing 64-KiB payload bound. Host must match IP:port.
No HTTP.sys reservation, elevation, firewall rule or profile change is performed.

## Android

Companion: https://github.com/Jau1Agiota/Peripheral-Battery-Companion/tree/codex/lan-preview

Build its E2-based preview with the PC address selected explicitly:

```powershell
.\gradlew.bat --no-daemon --max-workers=4 '-PlanPreviewHost=<PC_PRIVATE_IPV4>' testDebugUnitTest assembleDebug lintDebug
adb install -r app/build/outputs/apk/debug/app-debug.apk
```

Open the separate .lanpreview app, Settings, enter the approved PC IP/port and
connect. A debug-only explicit ADB launch can connect to the build-selected host:

```text
adb shell am start -n dev.peripheral.battery.lanpreview/dev.peripheral.battery.MainActivity --ez lan_preview_connect true
```

The shortcut uses port 8765. Debug network configuration denies cleartext by default
and permits only the selected numeric host; the client additionally enforces that
host/path and rejects redirects. A changed PC IP requires rebuilding. Release keeps
no INTERNET permission or cleartext exception. No custom trust-manager bypass.

Foreground/STARTED-only polling runs every two seconds after completion, with
2/5/15-second failure delays and cancellation. Response type/size/time are bounded.
Unchanged publications become stale after 30 seconds using local elapsed time.
Source freshness is not upgraded by HTTP receipt. Failures retain labelled stale
cache or unknown values; no demo fallback. No discovery, remote commands or ADB
network forwarding is used.

PC and phone must share a reachable LAN. If connection fails, check AP isolation,
selected interface and firewall policy. Any Private-profile rule needs separate
owner approval; do not silently change network profiles or open Public inbound.

## Verification

Baseline: 54 existing snapshot tests passed. New Windows socket/policy tests: 13
passed, including exact cached bytes, 503, method/path/Host/body/header rejection,
wildcard/loopback refusal and shutdown. Release build: zero warnings/errors.
Automated socket tests use controlled caches, not hardware or crypto acceptance.

Android E2 baseline: 223 JVM tests. Current preview: 233 JVM tests passed, zero
failures/errors/skips. Debug build/lint passed (zero errors, ten warnings).
Tests cover parsing reuse, polling/cancellation/backoff, malformed data, reconnect,
zero/null/precision, stale publication, destinations, redirects and response bounds.
Main APK installed on the connected API 36 phone. Xiaomi rejected the separate
instrumentation APK and shell touch injection; no security settings were changed.
Physical results are recorded below after verification. Full owner regression is
still required; no broad native/security suite was executed.


### Physical LAN check — 2026-11-02

The preview APK was installed on the connected Xiaomi/API 36 phone. Actual Wi-Fi
connections reached only the selected PC interface; no ADB forwarding or firewall/
network-profile change was used. The phone displayed G915 61%, G305 ~90% and HyperX
estimate 29%, matching contemporaneous Windows publications. HyperX also reported
0% during the session; both values were transported unchanged as ambiguous estimates.
This is source/transport agreement, not proof of headset battery accuracy/ownership.
A transient G915 source failure correctly showed 61% as stale retained data.

Explicit phone disconnect and a deliberately unreachable port retained stale values;
reconnecting to the original port restored Connected/live-preview presentation.
No demo fallback occurred. Actual Wi-Fi disable/enable, full Windows native tray
click/exit acceptance and instrumentation tests remain NOT RUN. Xiaomi restricted
test-APK installation/input injection; the main app's successful numeric-IP fetch
establishes on-device allow behavior, while other-host denial is covered by client
unit tests/generated configuration, not the blocked instrumentation assertions.

Foreground lifecycle observation: zero new phone TCP connections during six seconds
after background cancellation settled; three new connections in six seconds after
resume. This is a bounded observation, not a long-duration battery/network study.
