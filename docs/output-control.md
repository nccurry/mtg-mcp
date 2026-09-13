# MCP Output Control

> Historical reference: these removed legacy short-output aliases are not a
> rewrite compatibility requirement. Each approved child PLC defines its output
> limits and schema; stable `0.9.0` does not carry `compact` or workspace
> compatibility aliases just to preserve the old API.

Current tools use the `summary`, `normal`, and `full` values below when they
offer `detailLevel`.

Tools that expose `detailLevel` use one shared vocabulary:

| Value | Use |
| --- | --- |
| `summary` | Small bounded response for routine agent loops. |
| `normal` | Bounded response with supporting evidence or compact rows. |
| `full` | Raw or full-fidelity model payload. |

The released `0.8.x` server accepts legacy `compact` inputs as `summary`, and
`includeWorkspace=true` as a compatibility shortcut for `detailLevel=full`.
An older plan called this a `0.10.0` deprecation window; the clean-break `0.9.0`
program supersedes that forward-looking version claim. Maintenance of the
released legacy line may keep the aliases, but rewrite code does not reproduce
them.
