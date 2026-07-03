# Planned PLCs

Store PLC packets here while requirements, design, scope, and validation are
still being shaped.

Agents may update packets in this folder, but should not treat a planned PLC as
permission to start implementation unless the user asks for implementation.

## Evidence-First Rewrite Program

| Packet | Status | Summary |
| --- | --- | --- |
| [evidence-first-mcp-rewrite-program](evidence-first-mcp-rewrite-program/README.md) | Planned | Govern the one-at-a-time authoring and independent review of the clean-rewrite child PLCs. |

## Product Foundation Follow-ups

| Packet | Status | Summary |
| --- | --- | --- |
| [mcp-trust-evidence](mcp-trust-evidence/README.md) | Planned | Make source, derived, heuristic, and unknown evidence explicit. |
| [configurable-decision-models](configurable-decision-models/README.md) | Planned | Add bounded, versioned, replayable policy configuration without a general rules engine. |
| [provider-evidence-workflows](provider-evidence-workflows/README.md) | Planned | Standardize provider provenance and safety while preserving distinct source populations. |

## Jasmine Analysis Repair Packets

| Packet | Status | Summary |
| --- | --- | --- |
| [card-snapshot-integrity](card-snapshot-integrity/README.md) | Planned | Persist trustworthy root/face coverage and make provider hydration failure-safe. |
| [deck-count-contracts](deck-count-contracts/README.md) | Planned | Add one canonical count partition without breaking legacy count/role fields. |
| [land-entry-classification](land-entry-classification/README.md) | Planned | Correct shared conditional tapped-land classification. |
| [simulation-profile-evidence](simulation-profile-evidence/README.md) | Planned | Correct primary-category evidence, tag deduplication, routes, and ties. |
| [stats-lab-interaction-readiness](stats-lab-interaction-readiness/README.md) | Planned | Add pre-spend interaction access metrics while preserving 0.9 keys. |
| [conservative-goldfish-v2](conservative-goldfish-v2/README.md) | Planned | Replace optimistic goldfish paths atomically with one conservative kernel. |

See the [Jasmine analysis repair roadmap](../../plans/jasmine-analysis-repair-roadmap.md)
for finding ownership, dependencies, compatibility, and shared fixture policy.
