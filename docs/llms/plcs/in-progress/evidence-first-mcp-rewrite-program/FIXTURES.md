# Evidence-First MCP Rewrite Program Fixtures And Acceptance Matrix

This planning-only packet uses durable review artifacts and scenarios rather
than runtime payload fixtures.

## Fixture Inventory

| ID | Type | Location | Purpose | Owner | Update rule |
| --- | --- | --- | --- | --- | --- |
| FIX-PROGRAM-REGISTRY | Planning registry | [README.md](README.md#required-child-registry) | Proves the proposed twelve-child order, dependencies, and current status. | Program owner | Update with every child draft, review, or approval. |
| FIX-GUARDRAILS | Decision baseline | [README.md](README.md#program-guardrails) | Gives every child one authoritative cross-topic contract. | Repository owner | Change only through an approved umbrella amendment. |
| FIX-CHILD-CHECKLIST | Review checklist | [Child packet acceptance checklist](#child-packet-acceptance-checklist) | Makes child review consistent and objective. | Child reviewer | Amend with the umbrella if a shared requirement changes. |
| FIX-PROVIDER-CHECKLIST | Provider review checklist | [Provider child checklist](#provider-child-checklist) | Prevents provider safety and evidence gaps. | Provider child reviewer | Update when shared provider policy changes. |
| FIX-APPROVAL-RECORD | Approval schema | [SADD review state](SADD.md#review-and-approval-state) | Separates planning approval from implementation authority. | Child reviewer | Keep schema stable across required children. |
| FIX-PLC-TEMPLATE | Packet template | [PLC template](../../../templates/plc/README.md) | Supplies the required five-file shape. | mtg-mcp | Follow repository template updates. |
| FIX-TOOLSET-GUARDRAILS | Surface-governance baseline | [SADD capability toolsets](SADD.md#capability-toolset-governance) | Defines static selection, exact assignment, mode intersection, and capability reporting. | Repository owner | Change only through an approved umbrella amendment. |
| FIX-NORTH-STAR-CHECKLIST | Product acceptance checklist | [SADD north-star gate](SADD.md#north-star-acceptance-gate) | Prevents endpoint coverage from replacing useful evidence workflows. | Child reviewer | Apply to every remaining capability child. |

## Acceptance Matrix

| Requirement | Fixture or scenario | Expected result | Validation |
| --- | --- | --- | --- |
| PROG-001 | FIX-PROGRAM-REGISTRY | README and implementation plan contain the same twelve slugs in the same order after AMEND-005 acceptance. | Document comparison |
| PROG-002 | SCN-001 | Each child is complete and validated before the next draft begins. | Registry and packet inspection |
| PROG-003 | SCN-002 | Every drafted child remains independently reviewable and implementation-blocked until approval. | Registry and README inspection |
| PROG-004 | SCN-003 | Umbrella approval cannot satisfy child implementation authorization. | Lifecycle and approval inspection |
| PROG-005, PROG-006 | FIX-GUARDRAILS, SCN-005 | Children conform or the queue stops for an amendment. | Child review |
| PROG-007, PROG-009, PROG-014 | FIX-CHILD-CHECKLIST | Child packet is complete, narrow, and traceable. | Checklist review |
| PROG-008, PROG-016 | SCN-006 | Audit is approved before destructive implementation, and existing PLCs remain unchanged meanwhile. | Registry and Git diff inspection |
| PROG-010, PROG-011 | SCN-004 | Folder and status transitions match actual planning progress. | Path and README inspection |
| PROG-012, PROG-013 | FIX-PROGRAM-REGISTRY | Required and future topics remain distinct and current. | Registry inspection |
| PROG-015 | FIX-PROVIDER-CHECKLIST | Applicable provider concerns have explicit decisions and tests. | Provider child review |
| PROG-017, PROG-018, PROG-019, PROG-021 | FIX-TOOLSET-GUARDRAILS, SCN-009, SCN-010 | Every tool has one toolset; default/all/none and all modes reconcile with capability output; registration remains static. | Manifest, architecture, and official-client review |
| PROG-020 | FIX-NORTH-STAR-CHECKLIST, SCN-011 | Remaining child proves one useful composed LLM workflow and explicit evidence/decision boundaries. | Child review and fixture traceability |
| PROG-022, PROG-023, PROG-024 | FIX-GUARDRAILS, FIX-PROGRAM-REGISTRY, SCN-012 | Unified Scryfall evidence, explicit bulk acquisition, removed Tagger capability, and deferred local query evaluation agree across every active packet. | Cross-packet terminology, surface, and provider-boundary review |
| PROG-025 | FIX-GUARDRAILS, FIX-PROGRAM-REGISTRY, SCN-013 | Hardening remains independently reviewed, exact-only, legality-free, behavior-preserving, and prerequisite to statistics. | Child traceability, schema/surface, and dependency review |

## Child Packet Acceptance Checklist

A required child cannot be approved until all applicable checks pass:

- [ ] Exactly one narrow topic and its explicit non-goals are defined.
- [ ] Approved prerequisite packets and decisions are linked.
- [ ] Current code, tests, docs, and scoped instructions were re-inspected.
- [ ] Current-state evidence and reuse/removal disposition are recorded.
- [ ] Guardrail conformance has no unresolved conflict.
- [ ] README, SRD, SADD, IMPLEMENTATION_PLAN, and FIXTURES exist.
- [ ] No template placeholders remain.
- [ ] Must requirements have objective acceptance criteria.
- [ ] Must requirements map to design and validation.
- [ ] Public tools, resources, prompts, schemas, annotations, and modes are exact
      or explicitly unaffected.
- [ ] Every tool belongs to exactly one named toolset; default-enabled status
      and mode intersection are explicit.
- [ ] Tool-versus-resource choices are justified without introducing a generic
      router solely to reduce the numeric tool count.
- [ ] North-star acceptance identifies player questions, evidence class,
      determinism boundary, unknown states, MCP decision boundary, and one
      representative composed LLM workflow.
- [ ] Data ownership, persistence, dependencies, security, and privacy are
      resolved or explicitly not applicable.
- [ ] Unknown, unavailable, unsupported, empty, and partial states are distinct
      where relevant.
- [ ] Offline tests, fixtures, surface checks, and live-test boundaries are
      decision-complete.
- [ ] Migration, rollout, rollback, and cleanup are explicit.
- [ ] Planning approval record is complete.
- [ ] Relative links resolve and `git diff --check` passes.
- [ ] No production implementation is included or authorized by the child.

## Provider Child Checklist

Apply this checklist to Scryfall, Archidekt, Playgroup, Moxfield interchange,
and future popularity-source packets:

- [ ] Contract owner and official, observed, or unsupported status are stated.
- [ ] Authentication and secret lifetime are stated without exposing values.
- [ ] Permission and terms sensitivity are documented.
- [ ] User agent, pacing, concurrency, rate limits, and hard caps are explicit.
- [ ] Retry, backoff, circuit-breaker, and cancellation behavior are explicit.
- [ ] Cache ownership, freshness, invalidation, and provenance are explicit.
- [ ] Provider errors are sanitized and missing data is not invented.
- [ ] Sanitized fixture provenance and refresh rules are explicit.
- [ ] Ordinary tests are offline.
- [ ] Live tests are opt-in, safely scoped, and clean up mutations where
      supported.

## Review Scenarios

| ID | Scenario | Expected result |
| --- | --- | --- |
| SCN-001 | An agent begins a later child while the current packet is incomplete or unvalidated. | Later drafting stops until the current five-file packet passes its documentation checks. |
| SCN-002 | All children are drafted but have not been reviewed. | They remain separate `planned` packets with implementation authorization set to `No`. |
| SCN-003 | An implementer cites only the approved umbrella. | Production edits do not begin; the applicable child approval and explicit authorization are required. |
| SCN-004 | Child 1 drafting begins while the umbrella remains under `planned/`. | The umbrella moves to `in-progress/` in the same change and records the active child. |
| SCN-005 | A child requires a different operation mode or product boundary. | The queue is blocked pending an umbrella amendment and affected-child review. |
| SCN-006 | The foundation child proposes deleting legacy code before audit approval. | Review rejects the foundation draft or change until the audit allowlist is approved. |
| SCN-007 | All twelve child packets are approved but none is implemented. | The umbrella may complete, while each child's implementation lifecycle remains unchanged. |
| SCN-008 | A post-cutover topic is proposed during the required sequence. | It remains a registry entry unless the repository owner explicitly amends the program. |
| SCN-009 | Default selection starts after decks, Scryfall, and statistics are implemented. | Only those default-enabled toolsets intersected with the active mode are visible; provider integrations stay hidden. |
| SCN-010 | `all`, `none`, an explicit list, and an unknown name are started in each mode. | Stable implemented toolsets, zero tools, the exact requested subset, and sanitized startup failure are observed respectively; no `listChanged` capability appears. |
| SCN-011 | A child lists provider endpoints but does not state a player question, evidence class, or composed workflow. | Review rejects the child as north-star incomplete even when endpoint/schema traceability is otherwise complete. |
| SCN-012 | A planned document proposes a separate community-tag database, adapter, tool prefix/toolset, unsupported website traffic, automatic bulk download, or local execution of an uncached arbitrary Scryfall query. | Review rejects the document as conflicting with accepted AMEND-004; implementation remains blocked until the affected child is amended and approved. |
| SCN-013 | An implementer starts statistics before AMEND-005 and the hardening child are approved and completed, or adds legality/fuzzy identity behavior while hardening. | Work stops; child 9 remains the prerequisite and its explicit non-goals are restored. |

## MCP Surface Checks

| Profile | `read-only` | `local` | `remote` | Notes |
| --- | ---: | ---: | ---: | --- |
| Current implemented `default` | 22 | 43 | 43 | Default-enabled `decks` and `scryfall` toolsets after hardening. |
| Current implemented `all` | 47 | 69 | 82 | Adds the opt-in implemented `archidekt` and `playgroup` toolsets. |
| Planned final `default` | 32 | 54 | 54 | `decks,scryfall,stats` after hardening, statistics, and categorization. |
| Planned final `all` | 57 | 80 | 93 | All five target toolsets; derived from child matrices after AMEND-005. |
| `none` | 0 | 0 | 0 | Existing static-selection behavior remains. |

## Provider Fixtures

No provider payload is captured by the umbrella. Each provider-owning child
must create or identify sanitized fixtures after re-verifying its current
contract. URLs in the SRD are research starting points rather than accepted
payload baselines.

## Documentation Validation

- Confirm packet structure against `docs/llms/templates/plc/`.
- Search the packet for unresolved angle-bracket placeholders and generic
  template field labels.
- Resolve every relative Markdown link.
- Compare child slugs and order across README and IMPLEMENTATION_PLAN.
- Run `git diff --check`.
- Inspect the diff to confirm that umbrella/index documentation and the twelve
  required child packets agree; production changes require their active child.
- Search every non-completed packet and durable design document for obsolete
  Tagger storage/adapter/toolset/scraping claims and superseded surface totals.
