# Rewrite Skeleton And Repository Foundation Software Requirements Document

## Document Control

- Lifecycle status: Completed
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Last updated: 2026-07-03
- Related SADD: [SADD.md](SADD.md)
- Related implementation plan: [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)

## Purpose And Scope

The foundation creates the smallest green repository that can host later
evidence-first capabilities. It includes branch/worktree setup, project
boundaries, host configuration, evidence/result primitives, modes, minimal MCP
surface, and build/test/package wiring. It excludes every product capability
owned by later children.

## Requirements

| ID | Priority | Requirement | Acceptance criteria |
| --- | --- | --- | --- |
| FND-001 | Must | Implementation shall use branch `ncurry/evidence-first-mcp-rewrite` in sibling worktree `mtg-mcp-evidence-first-rewrite`, created from then-current `main`. | Worktree listing and branch ancestry prove the setup without disturbing the primary checkout. |
| FND-002 | Must | Git history and the stable legacy release shall remain available. | Branch has ordinary ancestry; no orphan/reset or legacy tag deletion occurs. |
| FND-003 | Must | The initial production solution shall contain only minimal `MtgMcp.Core` and `MtgMcp.App` projects. | Project and architecture tests reject legacy adapter/project references. |
| FND-004 | Must | Core shall have no runtime third-party package or App reference. | Project reference and package architecture tests pass. |
| FND-005 | Must | The MCP shall expose server identity through standard initialization and `mtg://server/capabilities`, with no tools or prompts. The capability document's resource count shall include itself. | Surface snapshot contains zero tools, one resource, zero prompts, and reports `resourceCount: 1`. |
| FND-006 | Must | Modes shall be `read-only`, `local`, and `remote`, with `local` as default. `read-only` forbids local and remote writes but may perform explicit provider network reads; it is not an offline mode. | Configuration, network-read, zero-write, and surface tests cover normalization and enforcement. |
| FND-007 | Must | Common result states shall distinguish success, not found, not cached, unsupported, unavailable, conflict, and invalid input. | Exhaustive union tests and output schemas cover every case. |
| FND-008 | Must | Evidence metadata shall distinguish source facts, source evidence, exact derivation, parser classification, heuristic estimate, and sampled estimate. | Serialization and exhaustive-switch tests pass. |
| FND-009 | Must | The default data root shall be platform application data under `mtg-mcp/v0.9`, overridable through `MTGMCP__DATA_DIR` and CLI configuration. | Cross-platform path unit tests and redacted server info pass. |
| FND-010 | Must | Credentials and absolute secret paths shall never appear in server output or logs. | Redaction and configuration-resource tests pass. |
| FND-011 | Must | Repository task, analyzer, 90-percent coverage, package, release, and official-client MCP E2E wiring shall remain green and shall enumerate the new project set rather than removed legacy adapters. | Per-assembly coverage conveniences, `test:integration`, and `surface:report` filters contain only existing projects/tests; `task lint`, `task test`, `task coverage`, `task pack`, `task smoke:process`, and `task smoke:mcp` pass. |
| FND-012 | Must | No automatic legacy data or tool-schema migration shall run. | Startup test with legacy data leaves it untouched and reports the clean-break boundary. |
| FND-013 | Must | Preview packages shall use `0.9.0-preview.N`; stable `0.9.0` remains reserved for cutover. | Version validation and package metadata tests pass. |
| FND-014 | Must | Legacy active product PLCs may receive audit-review supersession banners and cross-links, but lifecycle moves shall occur only as part of authorized foundation implementation. | Documentation diff records the new owner packet, leaves historical content intact, and performs no premature lifecycle move. |

## Quality Attributes

| Attribute | Measure |
| --- | --- |
| Minimality | Zero tools, one resource, two production projects. |
| Safety | Default mode cannot mutate remote systems; no provider is registered. |
| Modularity | Architecture tests enforce Core/App direction and reserved adapter boundaries. |
| Testability | All validation is deterministic and offline. |
| Release isolation | Preview install does not overwrite legacy data or stable package identity. |

## Traceability

| Requirements | Design | Validation |
| --- | --- | --- |
| FND-001, FND-002, FND-013 | Branch and release design | Git/worktree and package checks |
| FND-003, FND-004, FND-011 | Project boundaries | Architecture, lint, test, coverage |
| FND-005, FND-006 | MCP composition | Surface and E2E snapshots |
| FND-007, FND-008 | Core contracts | Unit and schema tests |
| FND-009, FND-010, FND-012 | Configuration/data boundary | Unit and process tests |
| FND-014 | Documentation lifecycle | PLC/index inspection |

## Definition Of Done

- [x] Every Must requirement passes.
- [x] The minimal preview package installs and starts an official-client session.
- [x] No legacy product capability is registered.
- [x] No later child capability is preimplemented.
