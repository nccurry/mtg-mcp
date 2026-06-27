# MCP Output Control

Tools that expose `detailLevel` use one shared vocabulary:

| Value | Use |
| --- | --- |
| `summary` | Small bounded response for routine agent loops. |
| `normal` | Bounded response with supporting evidence or compact rows. |
| `full` | Raw or full-fidelity model payload. |

During the 0.10.0 deprecation window, legacy `compact` inputs on older tools are
accepted as `summary`, and `includeWorkspace=true` is accepted as a compatibility
shortcut for `detailLevel=full`. New calls should use `detailLevel`.
