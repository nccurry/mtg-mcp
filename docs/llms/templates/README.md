# Planning Templates

Use these repository-owned templates for durable planning work. Current code,
tests, configuration, `AGENTS.md`, and human docs remain authoritative when a
template or completed plan becomes stale.

## Ordinary Plans

Copy [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md) into
`docs/llms/plans/<feature-slug>.md` for work that benefits from durable context
but does not need requirements and architecture documents.

## Plan-Led Changes

Copy the full [plc](plc/) directory into the correct lifecycle folder:

```text
docs/llms/plcs/planned/<feature-slug>/
  README.md
  SRD.md
  SADD.md
  IMPLEMENTATION_PLAN.md
  FIXTURES.md
```

`FIXTURES.md` is optional. Keep lifecycle status, scope, requirements,
traceability, validation, and completion evidence even when other optional
sections are removed.
