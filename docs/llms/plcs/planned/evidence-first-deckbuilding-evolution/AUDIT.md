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
| Adapter ownership | Needs rework. Scryfall and Archidekt have named owners that mostly forward to large context/database classes. |
| Reliability review | No P0 or P1 defect found in the audited scope. Existing operation modes, typed outcomes, explicit write guards, and fixture-backed tests are good foundations. |
| Dependencies | No known vulnerabilities. Several package updates, including a major MCP SDK update, need a focused compatibility review. |

## Findings

### ARCH-001 — Scryfall persistence ownership is nominal, not physical

- Severity: P2
- Status: Open
- Affected area: MtgMcp.Scryfall

The completed hardening design assigns connection/schema work to
[ScryfallDatabase](../../completed/mcp-contract-and-adapter-hardening/SADD.md#scryfall-building-blocks)
and corpus, snapshot, and coordination work to separate stores. In the current
source, [ScryfallStores.cs](../../../../../src/MtgMcp.Scryfall/ScryfallStores.cs)
only forwards calls to
[ScryfallDatabase.cs](../../../../../src/MtgMcp.Scryfall/ScryfallDatabase.cs).
That one class still owns corpus reads and imports, snapshots, cross-process
coordination, SQL helpers, and schema work.

The result is extra navigation without a real seam. A corpus change, snapshot
change, or pacing change all modifies the same large owner. The current tests
protect behavior, but the design makes future changes harder to isolate.

Planned disposition: keep one concrete SQLite connection/schema owner; move the
real corpus, snapshot, and coordination SQL operations into the named stores;
remove the forwarding bodies. Do not add a repository interface solely to
perform this move.

### ARCH-002 — Archidekt’s domain owners are forwarding layers around two god contexts

- Severity: P2
- Status: Open
- Affected area: MtgMcp.Archidekt

The current public facade correctly presents deck, folder, and snapshot
operations. However,
[ArchidektDeckOperations, ArchidektFolderOperations, and ArchidektSnapshotOperations](../../../../../src/MtgMcp.Archidekt/ArchidektFacade.cs)
all delegate to
[ArchidektOperationContext](../../../../../src/MtgMcp.Archidekt/ArchidektService.cs).
The same pattern appears in the transport layer:
[the named transports](../../../../../src/MtgMcp.Archidekt/ArchidektTransportFacade.cs)
forward to
[ArchidektTransportContext](../../../../../src/MtgMcp.Archidekt/ArchidektTransport.cs).

This misses the intended boundary documented in the completed hardening packet:
shared HTTP/pacing state should be one owner, while deck, folder, and snapshot
transport and workflow classes should own their actual behavior.

Planned disposition: retain ArchidektService as the stable public facade.
Replace the two contexts with one small shared HTTP/session owner and concrete
deck, folder, and snapshot transport/workflow classes containing their own
logic. Preserve the operation budget, authentication, retries, read-back
verification, and write safeguards exactly.

### DOC-001 — One architecture-test summary is stale

- Severity: P3
- Status: Open
- Affected area: test documentation

[FoundationArchitectureTests.cs](../../../../../tests/MtgMcp.Architecture.Tests/FoundationArchitectureTests.cs)
says “ninety-tool” in its summary while the test correctly asserts 93 tools.
The test itself passes, so this is documentation drift rather than a contract
failure.

Planned disposition: correct the summary in the first cleanup child and add
surface-count/documentation review to every future public-surface change.

### DEP-001 — Package updates need a compatibility plan, not a bulk bump

- Severity: P3
- Status: Deferred to a focused child
- Affected area: App, E2E tests, analyzers, test tooling

The dependency check reports ModelContextProtocol 2.2.0, ModelContextProtocol.Core
2.2.0, Roslynator 5.0.0, xUnit 4.0.0, Microsoft.NET.Test.Sdk 18.9.0, and
smaller analyzer updates. The vulnerability check reports none.

The MCP SDK is a major version change, while the current server has a carefully
tested static surface and installed-package smoke path. A bulk update alongside
the ownership refactor would make failures difficult to attribute.

Planned disposition: create an MCP SDK and toolchain compatibility child after
the ownership cleanup. It must pin target protocol behavior, run process and
official-client smoke tests, validate JSON schemas, and preserve the exact
toolset/mode contract before changing package versions.

### POLICY-001 — External-source expansion needs an admission gate

- Severity: P2 for future expansion; not a defect in the current stable release
- Status: Open design requirement

The existing adapters are bounded and documented. New source requests are
different: each has its own license, API shape, data meaning, cache rights, and
privacy requirements. Adding a generic “web research” adapter would make it
too easy to turn a question into unsupported scraping or blend unlike data.

Planned disposition: require an admission record and a narrow child PLC for
each provider. The record must prove supported access, source semantics,
retention/deletion rules, credential handling, pacing, fixture strategy, and
the evidence label shown to the client.

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
| Refactor | ScryfallDatabase and ScryfallStores | Give corpus, snapshot, and coordination stores real ownership. |
| Refactor | ArchidektOperationContext and ArchidektTransportContext | Move deck, folder, and snapshot behavior into actual domain owners. |
| Remove after extraction | Forwarding context methods and duplicate wrappers | They add indirection without a responsibility boundary. |
| Correct | Stale surface-count wording | Keep human documentation as accurate as the passing assertion. |
| Add | Characterization fixtures before owner movement | Prove unchanged results, SQL state, errors, pacing, and write guards. |
| Add later | One provider-admission record per external source | Make expansion safe, attributable, and reviewable. |
| Add only after feasibility | Isolated simulation-lab project and calibration fixtures | Keep sampled policy behavior out of Core and exact Statistics. |
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
