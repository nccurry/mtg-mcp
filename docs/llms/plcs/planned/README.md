# Planned PLCs

Store PLC packets here while requirements, design, scope, and validation are
still being shaped.

Agents may update packets in this folder, but should not treat a planned PLC as
permission to start implementation unless the user asks for implementation.
Ideas intentionally deferred beyond the rewrite live in the
[potential-features registry](../../../potential-features.md), not as implied
implementation work in these packets.

## Evidence-First Rewrite Children

Seven rewrite children remain planned and independently reviewable. The
[legacy audit](../completed/legacy-surface-audit-and-disposition/README.md) is
approved/completed, and the
[foundation child](../completed/rewrite-skeleton-foundation/README.md) is
approved/completed, and the
[local deck child](../completed/local-deck-store/README.md) is
approved/completed. The governing program remains
[in progress](../in-progress/evidence-first-mcp-rewrite-program/README.md).

| Packet | Status | Summary |
| --- | --- | --- |
| [manual-deck-interchange](manual-deck-interchange/README.md) | Draft | Define lossless native and explicit provider-compatible manual artifacts. |
| [scryfall-evidence-snapshots](scryfall-evidence-snapshots/README.md) | Draft | Define immutable snapshots of official Scryfall reads and rich source objects. |
| [archidekt-deck-sync](archidekt-deck-sync/README.md) | Draft | Define Archidekt deck sync, folder organization, named snapshots, and cleanup. |
| [playgroup-public-api](playgroup-public-api/README.md) | Draft | Define typed coverage of the pinned official Playgroup public API. |
| [exact-deck-statistics](exact-deck-statistics/README.md) | Draft | Define exact provider-independent deck probability and composition calculations. |
| [scryfall-tagger-cache](scryfall-tagger-cache/README.md) | Draft | Define deterministic cached Tagger evidence and bounded explicit acquisition. |
| [rewrite-stabilization-cutover](rewrite-stabilization-cutover/README.md) | Draft | Define cross-module acceptance, `0.9.0` release, rollback, and lifecycle gates. |

## Product Foundation Follow-ups

| Packet | Status | Summary |
| --- | --- | --- |
| [mcp-trust-evidence](mcp-trust-evidence/README.md) | Absorbed/reference-only | Evidence vocabulary rationale is owned by rewrite foundation/provider/statistics children; do not implement independently. |
| [configurable-decision-models](configurable-decision-models/README.md) | Post-cutover reference | Seed for a future experimental feasibility PLC; not stable `0.9.0` scope. |
| [provider-evidence-workflows](provider-evidence-workflows/README.md) | Absorbed/reference-only | Provider principles are owned by Scryfall, Archidekt, Playgroup, and Tagger children. |

## Jasmine Analysis Repair Packets

| Packet | Status | Summary |
| --- | --- | --- |
| [card-snapshot-integrity](card-snapshot-integrity/README.md) | Superseded/reference-only | Known-empty/unknown and root/face principles are absorbed by local deck and Scryfall children. |
| [deck-count-contracts](deck-count-contracts/README.md) | Superseded/reference-only | Zone-based storage and exact summaries remove the category-count root cause. |
| [land-entry-classification](land-entry-classification/README.md) | Reference-only | Exact statistics uses caller-supplied source masks and does not parse oracle text. |
| [simulation-profile-evidence](simulation-profile-evidence/README.md) | Post-cutover reference | Fixture input for future simulation feasibility; not stable scope. |
| [stats-lab-interaction-readiness](stats-lab-interaction-readiness/README.md) | Post-cutover reference | Heuristic sequencing taxonomy is deferred to future simulation feasibility. |
| [conservative-goldfish-v2](conservative-goldfish-v2/README.md) | Post-cutover design seed | Conservative kernel design may follow, but cannot precede, feasibility approval. |

See the [Jasmine analysis repair roadmap](../../plans/jasmine-analysis-repair-roadmap.md)
for finding ownership, dependencies, compatibility, and shared fixture policy.
