# W1 owner decision sheet — proposals, no approval inferred

[Evidence and recommendation](W1_GATE_P_BLOCKER_RESOLUTION.txt) ·
[Metadata/host/gate design](W1_GATE_P_METADATA_AND_HOST_GATES.md).

The public-leaf-only direction and TEST-ONLY ACL evidence NOT ESTABLISHED are already
accepted **for preparation** and are not reopened. Production NonExportable AND
CurrentUserAclRestricted remains unchanged. No real IP, serial, permit or operation
ID is selected/generated here. Nothing in a proposal is an execution default.

## Needed before a separate Gate I implementation request

| Decision | Conservative proposal | Current state / unresolved question |
| --- | --- | --- |
| Target selection | Windows x64 ordinary interactive/non-elevated/profile-loaded context; SDK10.0.402, Core runtime10.0.13, no roll-forward | Available per owner; will owner select these exact targets for W1? |
| Public API resource model | strict result acceptance lengths; no hard transient memory/native allocation cap claimed | Will owner accept post-call bounds, or require redesign before source implementation? |
| UI/time model | no requested UI, no retry/background work; no hard synchronous native timeout; no watchdog/helper/kill path | Will owner accept this disclosed limitation for the design, or require a separately scoped containment design? |
| Outcome/cleanup contract | per-check metadata/public-leaf observations; Dispose attempted/returned/uncertain recorded; native cleanup NOT PROVEN | Will owner accept a limited observation report with CleanupUnconfirmed and no overall security PASS, or require stronger cleanup proof before proceeding? |

Recommendation **REVISE until these explicit design choices are resolved**. Selecting
these does not grant Gate I itself. An eventual separate Gate I must approve exact
nine-file allowlist and inert boundaries. No native execution is needed to decide
these design constraints. No need to provide a LAN address/serial/date just to review
source structure; unresolved E-only values must force NOT RUN, never defaults.

## Needed only before separately approved Gate E

| Input | Conservative proposal for review | Exact owner value now |
| --- | --- | --- |
| SAN | exactly ONE canonical private RFC1918 IPv4 OR IPv6 ULA fc00::/7; bare address, no port/CIDR/zone/mapped form/DNS alias; TEST SAN only, no network connection | UNSET |
| Subject/issuer | same synthetic distinguished name, proposed CN=W1 Offline Public Leaf; no identity fallback | NOT SELECTED |
| Public serial | positive, nonzero unsigned big-endian, canonical minimal magnitude,1–16 bytes and high bit clear (keeps DER positive within20octets); no RNG/real serial generated here | UNSET |
| UTC validity | explicit owner-reviewed UTC notBefore/notAfter, positive duration at most10minutes; proposed preflight within interval with at least2minutes left | UNSET |
| Clock confidence | owner attests trusted UTC uncertainty<=30seconds for that attempt; any doubt/expiry stops before native, no time-service/network probe | NOT APPROVED |
| Extensions | SAN+serverAuth EKU noncritical; digitalSignature KU and non-CA/no-pathLen basicConstraints critical; no extra SAN/EKU/KU/extension | NOT SELECTED |
| Accepted public buffers | proposed public DER/SPKI/temporary-result aggregate64KiB; metadata per-return64bytes; sanitized result8KiB | NOT APPROVED; post-call/retention bounds only, not total-process/native cap |
| Result destination | owner-chosen new local result path outside Git/OneDrive/shared directories; no overwrite; report only phase/status/allowlisted numeric errors/public booleans | UNSET, no file/path fabricated |
| Consent | exact reviewed source/binaries/host/config/inputs/resource/API list, one manual attempt in one ordinary process; one-shot consumed before provider call | NO PERMIT / NO COMMAND / NO APPROVAL |
| UI/unresponsive native call | no dialog interaction, fallback or rerun; when control returns record STOP; no unattended invocation; crash/termination implies CleanupUnconfirmed | Exact owner response plan UNSET; no kill/helper action authorized |
| Cleanup | leaf -> signer duplicate -> original key in finally; independently record each attempt; uncertainty persists, stop without deletion/reopen/enumeration/adoption | NOT NATIVE-VALIDATED |

The buffer proposal needs precise accounting of framework temporaries before E;
GetProperty/DER creation can allocate before checking lengths. A stopwatch/Task
cancellation cannot interrupt a blocking native API. No fictitious deadline or
native release receipt may turn uncertainty into PASS. Dispose returning is managed
evidence only. Public leaf is not a TLS server identity; chain/trust/Android, export
denial, ACL and OS nonpersistence remain unproven even if public checks agree.

## Later Gate B evidence, not owner-provided values now

Resolved SDK/import/task/dependency manifests, lock/package hashes, actual generated
runtimeconfig/deps, exact x64 testhost/adapter/host and runtime assembly provenance
must be independently reviewed after individually approved phases. Exact selected/
loaded versions remain UNKNOWN now. No broad discovery/test/build command supplied.

## Source mapping disposition

P1 revision discrepancy resolved by official VMR tag + source manifest, with94 selected
files matching constituent sources. This does not establish reproducible-build
identity or owner's installed/loaded binaries. If later mapping cannot be closed,
proposed alternative is owner-reviewed source-provenance acceptance for CODE-ONLY
Gate I with B/E blocked pending binary reconciliation. It is not an automatic lower
standard and is not permission to run an exploratory native probe.

Do not use historical keys/journals/retired IDs, Option2, previews, native CI or
unfiltered legacy tests. D3.3A11+2 incident remains unchanged.

**NATIVE NOT RUN / TESTS NOT RUN / LAN NOT RUN / PRODUCTION SECURITY NOT PROVEN.**

Stop for owner review. No automatic implementation or execution, merge or release.
