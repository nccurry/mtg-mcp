# LLM And Agent Docs

This directory contains durable guidance for AI coding agents working in
`mtg-mcp`. The source of truth is still current code, tests, checked-in
configuration, `Taskfile.yml`, and [AGENTS.md](../../AGENTS.md).

For evidence-first rewrite work, first read the
[current-versus-target rewrite guide](../rewrite-guide.md) and the governing
[umbrella PLC](plcs/completed/evidence-first-mcp-rewrite-program/README.md).
Legacy implementation details remain in Git history. They are not current
architecture.

## Contents

- [agent-workflow.md](agent-workflow.md): planning, editing, validation, and
  handoff guidance.
- [csharp-best-practices.md](csharp-best-practices.md): C#/.NET conventions for
  this repository.
- [repo-operations.md](repo-operations.md): Task-based command policy.
- [plans](plans/): durable ordinary implementation plans.
- [plcs](plcs/): Plan-Led Change packets for larger work.
- [templates](templates/): reusable ordinary-plan and multi-file PLC templates.

## Local Scratch

Use ignored `/plans/` for local scratch notes, rough outlines, and discarded
experiments. Commit only plans that should guide future agents or reviewers.
