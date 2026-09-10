# Evidence-First Deckbuilding Evolution Audit Baseline

## Scope And Method

This audit examined the authored source tree, architecture tests, project
documents, task runner, package state, and representative provider boundaries.
It also used current official sources for MCP, .NET, Scryfall, Commander
Spellbook, Reddit, Moxfield, Magic rules, and probability.

Four pre-existing uncommitted Archidekt mapper/test edits were present before
the audit. They were left untouched. The structural findings below do not rely
on those files. Broad validation compiled and tested the current worktree, so
it is useful baseline evidence but not a review of those user changes.

## Baseline At A Glance

| Area | Result |
| --- | --- |
| Product boundary | Strong. Current docs correctly frame the server as evidence and workflow support, not a deckbuilding decider. |
| Build, lint, tests | Passed. 545 non-live tests passed. |
| Coverage | Passed. Each production assembly is above 90% line coverage. |
| MCP surface | Passed. 93 statically registered tools, one capability resource, zero prompts. |
| Core boundaries | Strong. Core has no adapter/host references; Statistics remains provider-independent. |
| Adapter ownership | Scryfall is complete. Archidekt still has named owners that forward to large context classes. |
| Reliability review | No P0 or P1 defect found in the audited scope. Existing operation modes, typed outcomes, explicit write guards, and fixture-backed tests are good foundations. |
| Dependencies | No known vulnerabilities. Several package updates, including a major MCP SDK update, need a focused compatibility review. |

## Findings

### ARCH-001 — Scryfall persistence ownership is nominal, not physical

- Severity: P2
- Status: Complete
- Affected area: MtgMcp.Scryfall

At the time of this audit, the named stores forwarded to one large database
class. The completed hardening design assigns connection/schema work to
[ScryfallDatabase](../../completed/mcp-contract-and-adapter-hardening/SADD.md#scryfall-building-blocks)
and card data, snapshots, and coordination work to separate stores.

This was resolved in the
[completed Phase 1A child](../../completed/scryfall-store-ownership-extraction/README.md).
ScryfallDatabase now owns only the path, connections, schema, and disposal.
The three named stores own their SQLite workflows, and a narrow reflection test
prevents the database from adding a domain method again.

### ARCH-002 — Archidekt domain owners were forwarding layers around two large contexts

- Severity: P2
- Status: Complete
- Affected area: MtgMcp.Archidekt

At audit time, the public facade presented deck, folder, and snapshot
operations while forwarding their behavior through two large context classes.
The same pattern existed in the transport layer.

This misses the intended boundary documented in the completed hardening packet:
shared HTTP/pacing state should be one owner, while deck, folder, and snapshot
transport and workflow classes should own their actual behavior.

The completed Phase 1B child retained `ArchidektService` as the stable public
facade. [ArchidektSession](../../../../../src/MtgMcp.Archidekt/ArchidektSession.cs)
now owns shared HTTP and pacing state. The named deck, folder, and snapshot
transport and operation classes own their routes and workflows. The old
contexts and forwarding layers are gone.

### DOC-001 — One architecture-test summary is stale

- Severity: P3
- Status: Complete
- Affected area: test documentation

[FoundationArchitectureTests.cs](../../../../../tests/MtgMcp.Architecture.Tests/FoundationArchitectureTests.cs)
used to say “ninety-tool” while the test correctly asserted 93 tools. The
completed Phase 1B child corrected that text.

Future public-surface changes still require a surface-count and documentation
review.

### DEP-001 — Package updates need a compatibility plan, not a bulk bump

- Severity: P3
- Status: Planned in a focused child
- Affected area: App, E2E tests, analyzers, test tooling

The dependency check reports ModelContextProtocol 2.2.0, ModelContextProtocol.Core
2.2.0, Roslynator 5.0.0, xUnit 4.0.0, Microsoft.NET.Test.Sdk 18.9.0, and
smaller analyzer updates. The vulnerability check reports none.

The MCP SDK is a major version change, while the current server has a carefully
tested static surface and installed-package smoke path. A bulk update alongside
the ownership refactor would make failures difficult to attribute.

The [latest MCP and toolchain child](../../completed/latest-mcp-and-toolchain/README.md)
completed this work. It pinned the current protocol, version locks, test path,
and package checks without mixing them with a provider change.

### SOURCE-001 — External-source expansion needs a clear source check

- Severity: P2 for future expansion; not a defect in the current stable release
- Status: Open design requirement

The existing adapters are bounded and documented. New source requests are
different: each has its own API shape, data meaning, cache behavior, and
privacy needs. Adding a generic “web research” adapter would make it too easy
to turn a question into unsupported scraping or blend unlike data.

Planned disposition: require a short source check and a narrow child PLC for
each provider. The record names supported access, source meaning, cache expiry,
credential handling, pacing, fixture strategy, and the evidence label shown to
the client.

## What To Keep

| Asset | Why it stays |
| --- | --- |
| The North Star and evidence taxonomy | They express the correct separation between facts, evidence, exact derivations, estimates, and unknowns. |
| Native C# OperationResult and EvidenceDescriptor unions | They make expected outcomes explicit and exhaustively matchable without a third-party result framework. |
| Static capability toolsets and operation modes | They keep relevance separate from authority and avoid a generic intent router. |
| Exact Statistics | Hypergeometric and related exact calculations are the right default for card-draw questions. |
| Existing read-back, fingerprint, preview/apply, and operation-budget safeguards | They make deck mutation auditable and reversible enough for the provider contract. |
| Offline, fixture-backed tests and per-assembly coverage gate | They give ownership refactors a reliable safety net. |

## What To Remove, Refactor, Or Add

| Action | Target | Reason |
| --- | --- | --- |
| Refactor | ScryfallDatabase and ScryfallStores | Give card-data, snapshot, and coordination stores real ownership. |
| Refactor | ArchidektOperationContext and ArchidektTransportContext | Move deck, folder, and snapshot behavior into actual domain owners. |
| Remove after extraction | Forwarding context methods and duplicate wrappers | They add indirection without a responsibility boundary. |
| Correct | Stale surface-count wording | Keep human documentation as accurate as the passing assertion. |
| Add | Characterization fixtures before owner movement | Prove unchanged results, SQL state, errors, pacing, and write guards. |
| Add later | One source check per external source | Make expansion safe, attributable, and reviewable. |
| Add only after independent review and owner authorization | Isolated MtgMcp.OnCurve project, real-deck fixtures, and one read-only deck tool | Keep sampled land rules out of Core and exact Statistics. |
| Do not add | Generic provider framework, scraper, rules engine, result library, or recommendation engine | They add abstraction cost or violate the product boundary. |

## Audit Lenses

| Lens | Result |
| --- | --- |
| Abstraction quality | ARCH-001 and ARCH-002 are the material findings. |
| Correctness and reliability | No P0/P1 fault found. Expected provider and operation outcomes already use typed results; cancellation and explicit mutation control are established patterns. |
| Code quality and visual readability | Named classes and comments are generally clear, but the forwarding “owners” obscure real responsibility. DOC-001 is the only proven drift. |
| Plain language | Product docs are direct and distinguish evidence from advice. No broad rewrite is needed. |
| Dead code | No confirmed authored production dead code was found. Ignored legacy build directories are not deletion targets. |
| Test coverage and quality | Strong baseline: 545 passing offline tests and seven production coverage gates. Refactor children still need behavior-first characterization tests. |
| Performance | No current measured hot-path regression was found. Add a benchmark only when a child introduces a meaningful risk, per the existing performance-ratchet policy. |
| Dependency health | No known vulnerable package. Updates exist and need a narrow compatibility effort. |
| Over-engineering | Do not respond to two oversized owners with a generic repository/service hierarchy. Use concrete vertical ownership instead. |

## Validation Evidence

| Check | Result |
| --- | --- |
| task lint | Passed |
| task test | Passed: 545 tests |
| task coverage | Passed: App 91.11%, Archidekt 91.07%, Core 99.39%, Decks 93.82%, Playgroup 95.81%, Scryfall 93.81%, Statistics 96.32% line coverage |
| task surface:report | Passed: 93 tools, one resource, zero prompts |
| task deps:check | Completed: updates reported, no failure |
| dotnet list package --vulnerable --include-transitive | Passed: no vulnerable packages reported |

## Audit Verdict

No emergency rewrite is warranted. The project has a sound product boundary,
healthy behavior checks, and good test coverage. It does need planned
structural rework before adding more provider or simulation complexity:
ownership must be real, source admission must be explicit, and experiments must
remain outside the factual core.
