# Packaged Live Acceptance

## Result

The packaged `0.9.0-preview.1` MCP passed method acceptance on 2026-07-12 at
commit `e0d68e7cf897430f9c43b4657307fd520469cbf7`.

| Status | Count |
| --- | ---: |
| `live-pass` | 88 |
| `fixture-backed-owner-approved` | 2 |
| `pending-provider-generation` | 1 |
| `fixture-only-owner-approved` | 2 |
| **Registered tools** | **93** |

The capability resource passed. The tested surface contained one resource and
zero prompts. The default profile contained 32, 54, and 54 tools in
`read-only`, `local`, and `remote`. The `all` profile contained 57, 80, and 93.

## Provider Results

- Archidekt deck, folder, snapshot, pull, push, restore, and cleanup workflows
  passed against owner-authorized disposable state. No disposable resource
  remained.
- All safe Playgroup reads passed. The run sent zero Playgroup writes. The two
  documented writes remain fixture-only because the public API has no cleanup.
- Bounded Scryfall reads and retained evidence passed. Corpus sync and delete
  retain fixture-backed approval. Live rollback remains
  `pending-provider-generation` until Scryfall publishes a second generation;
  deterministic rollback fixtures pass.
- The retained Scryfall database was not modified by scratch acceptance.

## Redaction

Tracked evidence contains no credentials, account identities, provider URLs,
remote resource identifiers, local paths, or raw provider payloads.
