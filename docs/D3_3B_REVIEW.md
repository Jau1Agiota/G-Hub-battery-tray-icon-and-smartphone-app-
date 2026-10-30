# D3.3B native proof design — no execution authorization

[Full design, experiment matrix, test inventory and decision record](D3_3B_NATIVE_PROOF_DESIGN_REPORT.txt)

The same full report and shared decision matrix are versioned in both repositories:

- [Windows design branch](https://github.com/Jau1Agiota/G-Hub-battery-tray-icon-and-smartphone-app-/tree/feature/d3-3b-native-proof-design/docs)
- [Android design branch](https://github.com/Jau1Agiota/Peripheral-Battery-Companion/tree/feature/d3-3b-native-proof-design/docs)

Documentation-only delta from Windows D3.3A `60dc9da8c55a8cdbb3046849badb8881d60ea706`
and Android D3.3A `38351f32e5941d4adb053746521fdb73e94431ae`. Remote heads matched;
the baseline PRs remained draft, open and unmerged when inspected.

No source, tests, CI, prior reports or production configuration changed. No builds,
runtime tests or scripts ran. `[skip ci]` prevents the new documentation branch
from selecting the existing full Windows workflow; CI configuration is untouched.

The D3.3A legacy native-test incident remains explicitly documented. The safe-test
inventory distinguishes actual callees, file/digest operations, native allocations,
OS-store inspection, process simulation and unknown paths. Proposed selectors are
documentation, not permission to execute them.

Next-stage recommendation: **REVISE / NO-GO to native execution**. First resolve
the nonexportability versus exportable-envelope conflict and DNS versus IP-SAN
policy conflict, review the exact ephemeral lifecycle, then separately approve
one offline Windows lifecycle experiment if appropriate. No blanket approval.
Android anchoring/pre-credential enforcement and durable anti-rollback recovery
remain unproven; restored credentials stay `RESTART_LOCKED`.

**NATIVE NOT RUN. LAN NOT RUN. PRODUCTION SECURITY NOT PROVEN.**

Stop for owner review. No merge, release or automatic progression.
