# ADR 0001: Record Architecture Decisions

Status: Accepted

Date: 2026-06-27

## Context

`mtg-mcp` is moving through a pre-1.0 architecture cleanup that changes MCP
surface shape, result contracts, source adapters, and service boundaries. The
project already has stable principles in `AGENTS.md` and `docs/architecture.md`,
but future phases need a durable place to record why a contract or boundary was
chosen.

Two decisions are especially important to preserve:

- The server is evidence-first. It returns grounded rows, labels, assumptions,
  warnings, and deterministic metadata; the calling LLM does synthesis and
  judgment for the user.
- Recommendation sources are API-only. The project does not scrape HTML, parse
  browser pages, or automate web UIs for source data.

## Decision

Use lightweight ADRs in `docs/adr/` for decisions that affect public MCP
contracts, architectural boundaries, source policy, data persistence, security
gates, release policy, or meaningful trade-offs between competing designs.

ADRs use the template in `docs/adr/template.md`. Each ADR records status,
context, decision, consequences, and considered alternatives.

## Consequences

Future phases can change the architecture without losing the rationale behind
pre-1.0 choices. The ADRs should stay short and specific; routine implementation
details and obvious local refactors do not need an ADR.

Public surface changes still require README, changelog, and test updates under
`docs/versioning.md`.

## Alternatives Considered

- Keep decisions only in issue or PR descriptions. This is easy during active
  work but hard to find after a release.
- Put all rationale into `docs/architecture.md`. That keeps one document, but it
  turns stable architecture docs into a chronological change log.
