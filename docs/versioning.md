# Versioning and MCP Surface Changes

`mtg-mcp` is pre-1.0. Minor versions may still change public MCP tools,
resources, prompts, parameter names, and result shapes, but every change should
be intentional, documented, and easy for agents to reason about.

## Public Surface

The public surface includes:

- MCP tool names, descriptions, annotations, parameters, and result shapes.
- MCP resource URI templates, descriptions, and payload shapes.
- MCP prompt names, descriptions, parameters, and generated workflow guidance.
- CLI commands, environment variables, config keys, data file formats, and
  package/install behavior.

Internal C# type names are not public unless they are serialized into one of
those surfaces or documented for extension authors.

## Compatibility Policy Before 1.0

- Patch releases should be bug fixes, docs, test hardening, and additive fields.
- Minor releases may remove or rename MCP surface entries when the changelog,
  README, and surface tests are updated in the same change.
- Breaking changes should prefer a deprecation minor before a removal minor when
  the old shape has shipped in a tagged release.
- Unreleased branch-only APIs may be replaced directly instead of carrying
  compatibility shims.

## Deprecating Tools or Parameters

When a shipped tool, resource, prompt, parameter, or result field is being
removed or renamed:

1. Mark the old description with `Deprecated:` and name the replacement.
2. Add a changelog entry under `Unreleased` with the deprecation version and
   planned removal version.
3. Keep the old surface working through at least one minor release unless the
   change fixes a safety issue or a clearly broken contract.
4. Add or update tests proving both the old and new behavior during the
   deprecation window.
5. Remove the old surface only in the planned removal release and update the
   README complete surface list in the same change.

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

- Run `task lint`, `task test`, and `task smoke:mcp`.
- Confirm `README.md` covers every registered tool/resource/prompt.
- Confirm `CHANGELOG.md` describes public surface changes.
- Build the package and run the local install smoke path from `Taskfile.yml`.
