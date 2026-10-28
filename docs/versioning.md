# Versioning and Public MCP Changes

`mtg-mcp` is pre-1.0. Minor versions may still change public MCP tools,
resources, prompts, parameter names, and result shapes, but every change should
be intentional, documented, and easy for agents to reason about.

## Public MCP Contract

The public MCP contract includes:

- MCP tool names, descriptions, annotations, parameters, and result shapes.
- MCP resource URI templates, descriptions, and payload shapes.
- MCP prompt names, descriptions, parameters, and generated workflow guidance.
- CLI commands, environment variables, config keys, data file formats, and
  package/install behavior.

Internal C# type names are not public unless they are serialized into one of
those tools and APIs or documented for extension authors.

## Compatibility Policy Before 1.0

- Patch releases should be bug fixes, docs, test hardening, and additive fields.
- Minor releases may remove or rename MCP tools or API entries when the
  changelog, README, and tool/API tests are updated in the same change.
- Breaking changes should prefer a deprecation minor before a removal minor when
  the old shape has shipped in a tagged release.
- Unreleased branch-only APIs may be replaced directly instead of carrying
  compatibility shims.

### Approved `0.9.0` clean-break exception

The evidence-first `0.9.0` program intentionally replaces the legacy product
API without deprecation aliases or automatic data, configuration, or tool
migration.
Its approved umbrella and child PLCs superseded the ordinary deprecation steps
below for that rewrite only. That cutover is complete. Current-server
maintenance follows the ordinary policy.

The completed rewrite required exact schema review, preview releases,
user-facing clean-break documentation, rollback to the prior package/data, and
history-preserving Git integration. See [rewrite-guide.md](rewrite-guide.md).

### Approved `0.11.0` focused-agent-tool-surface exception

The approved focused-agent-tool-surface program (see
`docs/llms/plcs/in-progress/focused-agent-tool-surface/`) renames, merges, and
removes tools, and changes default selection, in one release without
deprecation aliases. The owner approved this clean break for that program
only. Old names return an unknown-tool error, and the changelog lists every old
name beside its replacement. Changes after this program follow the ordinary
policy.

## Deprecating Tools or Parameters

When a shipped tool, resource, prompt, parameter, or result field is being
removed or renamed:

1. Mark the old description with `Deprecated:` and name the replacement.
2. Add a changelog entry under `Unreleased` with the deprecation version and
   planned removal version.
3. Keep the old tool or API working through at least one minor release unless the
   change fixes a safety issue or a clearly broken contract.
4. Add or update tests proving both the old and new behavior during the
   deprecation window.
5. Remove the old tool or API only in the planned removal release and update
   the complete README list in the same change.

## Result Shape Changes

Additive fields are allowed in minor and patch releases, but they still need a
changelog entry when they are visible to MCP clients. Replacing a scalar with an
object, changing status strings, removing fields, or changing default
`detailLevel` output is breaking and follows the deprecation policy.

Use explicit metadata when a tool is narrower than its name suggests. For
example, a narrow evaluator should return evaluator/applicability fields instead
of relying on a score of `0` to imply "not applicable."

## Release Checklist

Before tagging a release:

- Run `task lint`, `task test`, `task smoke:process`, and `task smoke:mcp`.
- Confirm `README.md` covers every registered tool/resource/prompt.
- Confirm `CHANGELOG.md` describes public tool and API changes.
- Build the package and run `task release:tool-smoke` to probe and establish an
  MCP session against the installed tool.
- For a local workstation install, run `task install`. It also starts a
  read-only Scryfall-enabled server to catch self-contained startup failures.
