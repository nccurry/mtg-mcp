# Rewrite Skeleton And Repository Foundation Fixtures And Acceptance Matrix

## Fixture Inventory

| ID | Fixture | Expected result |
| --- | --- | --- |
| FND-SURFACE-001 | MCP initialize/list surface | Standard server identity; zero tools; `mtg://server/capabilities`; zero prompts; capability payload reports one resource including itself. |
| FND-MODE-001 | Mode omitted | Effective mode is `local`. |
| FND-MODE-002 | Each valid mode | Exact normalized value and allowed capability class. |
| FND-MODE-003 | Unknown mode | Startup fails with sanitized invalid-configuration result. |
| FND-EVIDENCE-001 | One payload per evidence union case | Stable case discriminator and applicable metadata only. |
| FND-RESULT-001 | One payload per operation-result case | Empty, unknown, unavailable, and error cases remain distinct. |
| FND-DATA-001 | Empty versioned data root | Server starts without creating future databases. |
| FND-DATA-002 | Legacy data root present | Legacy files remain byte-identical and are not loaded. |
| FND-DATA-003 | Configured root is a regular file | Startup rejects it with a path-free invalid-data-root failure. |
| FND-CLI-001 | Pair and equals switch forms plus duplicate/unknown/incomplete variants | Both valid forms agree; ambiguous input fails without echoing values. |
| FND-SECRET-001 | Config contains representative tokens and paths | Tool/resource/log output contains no secret value. |
| FND-PACK-001 | `0.9.0-preview.N` package | Contains only Core/App assemblies and required runtime assets; installed process probe and official-client resource read pass. |
| FND-TASK-001 | Rewritten task/project inventory | No coverage convenience, integration list, or surface-report filter names a removed project/test; new production assemblies are gateable. |

## Architecture Matrix

| From | May reference | Must not reference |
| --- | --- | --- |
| Core | BCL | App, MCP SDK, adapters, SQLite, HTTP transport types |
| App | Core, hosting, MCP SDK | Future capability code not yet implemented |
| Tests | Owning production project and test libraries | Live providers in normal tests |

## Acceptance Matrix

| Requirement | Fixture/check |
| --- | --- |
| FND-001, FND-002 | Git ancestry and worktree inspection |
| FND-003, FND-004 | Project/package architecture tests |
| FND-005, FND-006 | FND-SURFACE-001, FND-MODE-* |
| FND-007, FND-008 | FND-RESULT-001, FND-EVIDENCE-001 |
| FND-009, FND-010, FND-012 | FND-DATA-*, FND-SECRET-001 |
| FND-011, FND-013 | FND-TASK-001, full task gates, and FND-PACK-001 |
| FND-014 | PLC lifecycle/index inspection |

## Live Tests

None. The foundation registers no provider and performs no remote operation.
