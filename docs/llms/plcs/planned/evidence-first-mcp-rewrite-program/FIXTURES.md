# Evidence-First MCP Rewrite Program Fixtures And Acceptance Matrix

This planning-only packet uses durable review artifacts and scenarios rather
than runtime payload fixtures.

## Fixture Inventory

| ID | Type | Location | Purpose | Owner | Update rule |
| --- | --- | --- | --- | --- | --- |
| FIX-PROGRAM-REGISTRY | Planning registry | [README.md](README.md#required-child-registry) | Proves the ten-child order, dependencies, and current status. | Program owner | Update with every child draft, review, or approval. |
| FIX-GUARDRAILS | Decision baseline | [README.md](README.md#program-guardrails) | Gives every child one authoritative cross-topic contract. | Repository owner | Change only through an approved umbrella amendment. |
| FIX-CHILD-CHECKLIST | Review checklist | [Child packet acceptance checklist](#child-packet-acceptance-checklist) | Makes child review consistent and objective. | Child reviewer | Amend with the umbrella if a shared requirement changes. |
| FIX-PROVIDER-CHECKLIST | Provider review checklist | [Provider child checklist](#provider-child-checklist) | Prevents provider safety and evidence gaps. | Provider child reviewer | Update when shared provider policy changes. |
| FIX-APPROVAL-RECORD | Approval schema | [SADD review state](SADD.md#review-and-approval-state) | Separates planning approval from implementation authority. | Child reviewer | Keep schema stable across required children. |
| FIX-PLC-TEMPLATE | Packet template | [PLC template](../../../templates/plc/README.md) | Supplies the required five-file shape. | mtg-mcp | Follow repository template updates. |

## Acceptance Matrix

| Requirement | Fixture or scenario | Expected result | Validation |
| --- | --- | --- | --- |
| PROG-001 | FIX-PROGRAM-REGISTRY | README and implementation plan contain the same ten slugs in the same order. | Document comparison |
| PROG-002 | SCN-001 | A child-authoring change creates at most one child directory. | Git diff inspection |
| PROG-003 | SCN-002 | A later child is blocked until the preceding approval record is complete. | Registry and README inspection |
| PROG-004 | SCN-003 | Umbrella approval cannot satisfy child implementation authorization. | Lifecycle and approval inspection |
| PROG-005, PROG-006 | FIX-GUARDRAILS, SCN-005 | Children conform or the queue stops for an amendment. | Child review |
| PROG-007, PROG-009, PROG-014 | FIX-CHILD-CHECKLIST | Child packet is complete, narrow, and traceable. | Checklist review |
| PROG-008, PROG-016 | SCN-006 | Audit is approved before deletion planning, and existing PLCs remain unchanged meanwhile. | Registry and Git diff inspection |
| PROG-010, PROG-011 | SCN-004 | Folder and status transitions match actual planning progress. | Path and README inspection |
| PROG-012, PROG-013 | FIX-PROGRAM-REGISTRY | Required and future topics remain distinct and current. | Registry inspection |
| PROG-015 | FIX-PROVIDER-CHECKLIST | Applicable provider concerns have explicit decisions and tests. | Provider child review |

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
- [ ] Data ownership, persistence, dependencies, security, and privacy are
      resolved or explicitly not applicable.
- [ ] Unknown, unavailable, unsupported, empty, and partial states are distinct
      where relevant.
- [ ] Offline tests, fixtures, surface checks, and live-test boundaries are
      decision-complete.
- [ ] Migration, rollout, rollback, and cleanup are explicit.
- [ ] Planning approval record is complete.
- [ ] Relative links resolve and `git diff --check` passes.
- [ ] No production implementation or second child packet is included.

## Provider Child Checklist

Apply this checklist to Scryfall, Archidekt, Playgroup, Moxfield interchange,
Tagger, and future popularity-source packets:

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
| SCN-001 | An agent proposes two new child directories in one session. | Review blocks the change until it contains only one new child. |
| SCN-002 | The next child is requested while the current child is Draft or Changes requested. | The next child is not drafted; review work continues on the current child. |
| SCN-003 | An implementer cites only the approved umbrella. | Production edits do not begin; the applicable child approval and explicit authorization are required. |
| SCN-004 | Child 1 drafting begins while the umbrella remains under `planned/`. | The umbrella moves to `in-progress/` in the same change and records the active child. |
| SCN-005 | A child requires a different operation mode or product boundary. | The queue is blocked pending an umbrella amendment and affected-child review. |
| SCN-006 | The foundation child proposes deleting legacy code before audit approval. | Review rejects the foundation draft or change until the audit allowlist is approved. |
| SCN-007 | All ten child packets are approved but none is implemented. | The umbrella may complete, while each child's implementation lifecycle remains unchanged. |
| SCN-008 | A post-cutover topic is proposed during the required sequence. | It remains a registry entry unless the repository owner explicitly amends the program. |

## MCP Surface Checks

| Surface | Mode | Expected visibility | Notes |
| --- | --- | --- | --- |
| All existing MCP surfaces | Existing behavior | Unchanged | The umbrella and child-authoring work are documentation-only. |

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
- Inspect the diff to confirm that only documentation changed and no child PLC
  was created with this umbrella.
