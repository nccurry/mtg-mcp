# Playgroup Public API Software Requirements Document

## Document Control

- Lifecycle status: Planned
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Last updated: 2026-07-04
- Related SADD: [SADD.md](SADD.md)
- Related implementation plan: [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)

## Scope

In scope are the fifteen pinned official operations, bearer authentication,
provider-shaped models, pagination, provenance, errors, contract drift, and
safe write gating. Local deck synchronization, Archidekt hydration, derived
rankings, deck updates, private endpoints, and polling/background sessions are
out of scope.

## Requirements

| ID | Priority | Requirement | Acceptance criteria |
| --- | --- | --- | --- |
| PLAY-001 | Must | The checked-in OpenAPI fixture shall match the pinned version, size, and checksum. | Contract fixture test passes. |
| PLAY-002 | Must | Every pinned operation ID shall map to exactly one typed prefixed tool, and App shall additionally expose one redacted local `playgroup_auth_status` tool. | Operation-to-surface test reports fifteen provider matches, one status tool, sixteen total tools, and no extras. |
| PLAY-003 | Must | Response models shall preserve provider IDs, values, nullable fields, pagination, and unknown extension fields. | Fixture round trips pass. |
| PLAY-004 | Must | Every result shall include retrieval time, endpoint, API version, and provider limitations. | Schema snapshots pass. |
| PLAY-005 | Must | Authentication shall use `Authorization: Bearer` from host secret configuration and expose only redacted availability. | Header and redaction tests pass. |
| PLAY-006 | Must | Read operations shall not automatically fan out, hydrate another provider, or compute deck-quality rankings. | Network-spy and surface tests pass. |
| PLAY-007 | Must | Provider pagination parameters and maximums shall be preserved; outputs shall remain bounded. | Boundary fixtures pass. |
| PLAY-008 | Must | Event batch and live session creation shall require `remote` mode. | Mode tests issue zero writes outside remote. |
| PLAY-009 | Must | Write requests shall never be automatically retried after any response or ambiguous transport failure. | Fake HTTP request count is one. |
| PLAY-010 | Must | GET requests may retry transient transport/5xx at most twice with cancellation; 401/403 stop; 429 follows one bounded Retry-After only when present. | Fake HTTP tests pass. |
| PLAY-011 | Must | The client shall serialize requests and wait at least 250 ms between starts. This is a conservative client default because the pinned OpenAPI publishes no rate guidance; official stricter guidance shall supersede it. | Fake-clock concurrency tests and contract-note review pass. |
| PLAY-012 | Must | Missing deck update capability shall be reported as unsupported in capability output; no private endpoint shall be called. | Capability and network-spy tests pass. |
| PLAY-013 | Must | OpenAPI drift shall fail a contract check and require reviewed fixture/model/tool updates to the best current design; it shall not preserve obsolete operations solely for compatibility. | Altered-spec test fails with operation/schema diff until the reviewed current contract is adopted. |
| PLAY-014 | Must | Provider errors shall retain safe status/reason detail while redacting keys, users' private data, and local secret paths. | Sanitized error fixtures pass. |
| PLAY-015 | Must | Ordinary tests shall be offline. Safe authenticated reads may have opt-in live tests. Against the pinned 2026-07-03 contract, both writes shall remain fixture-only because the official API exposes no documented cleanup operation; evidence shall cite the owner decision and shall never label a write live-tested. | Test discovery, no-write live guards, contract fixtures, and owner-decision record pass. |
| PLAY-016 | Must | Every tool shall belong only to the opt-in `playgroup` toolset, toolset selection shall never widen operation-mode authority, and the auth/provider-read/evidence-correlation workflow shall pass the packet's north-star acceptance check without aliases, ranking helpers, or a generic router. | Default/all/explicit/none profile tests, per-mode write spies, and the composed provider-evidence fixture pass. |

## Quality Attributes

| Attribute | Measure |
| --- | --- |
| Completeness | Fifteen pinned operations, fifteen operation tools, one adapter-status tool, sixteen registered tools. |
| Fidelity | Provider fields and unknown extensions preserved. |
| Safety | Remote-only writes, no automatic write retries, no private endpoints. |
| Boundedness | Provider page limits preserved; no hidden fan-out. |
| Drift visibility | Pinned checksum and operation/schema diff. |

## Definition Of Done

- [ ] Pinned specification and all operation fixtures pass.
- [ ] No local ranking or deck-update emulation exists.
- [ ] Write mode and retry safety are proven.
- [ ] Live tests are discoverable but opt-in.
- [ ] Toolset assignment and the north-star acceptance workflow are proven.
