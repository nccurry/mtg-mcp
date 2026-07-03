# Agent Quality Foundation Fixtures And Acceptance Matrix

Lifecycle status: Completed.

## Acceptance Matrix

| ID | Requirement | Scenario | Expected result |
| --- | --- | --- | --- |
| DOC-001 | REQ-001 | Read north-star docs without implementation context. | Evidence categories and non-goals are unambiguous. |
| AGENT-001 | REQ-002 | Discover instructions from Core and App directories. | Root, source, and project-local rules appear in precedence order. |
| LLM-001 | REQ-003 | Parse `llms.txt` as Markdown. | H1, summary, linked sections, and Optional section are present. |
| PLC-001 | REQ-004 | Start a five-file packet from templates. | Requirements, design, phases, and fixtures have stable homes. |
| LINT-001 | REQ-005 | Remove XML documentation from a private member. | SA1600 fails the strict build. |
| LINT-002 | REQ-006 | Introduce formatting drift or excessive nesting. | `task lint` fails. |
| COV-001 | REQ-007 | Report any production assembly below 90 percent. | `task coverage:gates` fails and names the assembly. |
| MODE-001 | REQ-008 | Construct default options. | Effective mode is `plan`. |
| MODE-002 | REQ-008 | Start the MCP process without an operation-mode variable. | Apply-only tools are not advertised. |
| MODE-003 | REQ-008 | Configure explicit `apply`. | Apply-only tools remain available. |
| SAFE-001 | REQ-009 | Run the normal test workflow offline. | No live provider call or real Archidekt mutation occurs. |

## Coverage Baseline

| Assembly | Initial observed line coverage |
| --- | ---: |
| MtgMcp.Core | 88.47% |
| MtgMcp.Scryfall | 86.62% |
| MtgMcp.Archidekt | 88.99% |
| MtgMcp.Decklists | 89.07% |
| MtgMcp.Moxfield | 78.03% |
| MtgMcp.Playgroup | 80.95% |
| MtgMcp.App | 65.42% from App unit tests alone |
| MtgMcp.CommanderSpellbook | Not measured by the initial canonical configuration |

Canonical implementation measurements replace these exploratory values.

## Final Canonical Coverage

| Assembly | Line | Branch | Method |
| --- | ---: | ---: | ---: |
| MtgMcp.App | 90.52% | 77.75% | 95.69% |
| MtgMcp.Core | 90.69% | 79.55% | 93.62% |
| MtgMcp.Scryfall | 91.45% | 72.18% | 96.32% |
| MtgMcp.Archidekt | 91.46% | 69.13% | 97.97% |
| MtgMcp.Moxfield | 95.74% | 84.42% | 100.00% |
| MtgMcp.Playgroup | 98.62% | 81.11% | 100.00% |
| MtgMcp.CommanderSpellbook | 92.93% | 73.08% | 100.00% |
| MtgMcp.Decklists | 91.21% | 64.84% | 98.75% |
