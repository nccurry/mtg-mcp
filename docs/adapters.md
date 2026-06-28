# Adapter Operations

mtg-mcp adapters talk to public or user-configured services with provider-local
contracts. Core owns shared domain models and safe helper defaults; adapter projects own
third-party HTTP request and response shapes.

## User-Agent defaults

Adapters use a shared default User-Agent:

```text
mtg-mcp/<version> (+https://github.com/nccurry/mtg-mcp)
```

When the host assembly version is not available, the fallback version is `0.0.0`. Override
the value only when a provider asks for a specific contact string or a local deployment
needs a recognizable identifier.

Supported override keys:

| Adapter or source | Canonical config key | Short alias |
|---|---|---|
| Scryfall | `MtgMcp:Scryfall:UserAgent` | `SCRYFALL:USER_AGENT` |
| Archidekt | `MtgMcp:Archidekt:UserAgent` | `ARCHIDEKT:USER_AGENT` |
| Moxfield | `MtgMcp:Moxfield:UserAgent` | `MOXFIELD:USER_AGENT` |
| Playgroup | `MtgMcp:Playgroup:UserAgent` | `PLAYGROUP:USER_AGENT` |
| Commander Spellbook | `MtgMcp:CommanderSpellbook:UserAgent` | `COMMANDERSPELLBOOK:USER_AGENT` |
| TopDeck source | `MtgMcp:Intelligence:Sources:TopDeck:UserAgent` | `INTELLIGENCE:SOURCES:TOPDECK:USER_AGENT` |
| EDHREC source | `MtgMcp:Intelligence:Sources:Edhrec:UserAgent` | `INTELLIGENCE:SOURCES:EDHREC:USER_AGENT` |
| EDHTop16 source | `MtgMcp:Intelligence:Sources:EdhTop16:UserAgent` | `INTELLIGENCE:SOURCES:EDHTOP16:USER_AGENT` |

When setting these from a shell, prepend `MTGMCP__` and write `:` as `__`, such as
`MTGMCP__MOXFIELD__USER_AGENT`.

## Archidekt card-id cache

Archidekt writeback needs Archidekt's provider-specific card ids when adding cards to a
deck. Imported workspaces usually start with provider-neutral card facts such as Scryfall
ids, printed set/collector numbers, and names, so the Archidekt adapter resolves missing
Archidekt ids through Archidekt card search before mutation calls.

Those resolved ids are stored in an adapter-local card-id cache. This cache is deliberately
separate from the shared recommendation source-fact cache:

- It stores mutation support state, not recommendation evidence.
- Entries are keyed by Scryfall id, printed set/collector number, and card name so future
  writebacks can avoid repeated Archidekt card search requests.
- Structured entries include source, timestamp, card name, Scryfall uid, Archidekt id, and
  validation status; older string-only entries are upgraded when read.
- If Archidekt rejects a mutation because a cached id is stale, the adapter evicts the
  suspect ids, re-resolves them once, and retries the mutation batch.
- Normal tests use temporary cache files and do not mutate real Archidekt decks.

Configuration:

| Setting | Default | Notes |
|---|---|---|
| `MtgMcp:Archidekt:CardIdCacheFile` / `ARCHIDEKT:CARD_ID_CACHE_FILE` | user-local `mtg-mcp/archidekt-card-ids.json` | Override for hermetic tests, service accounts, or shared installations that need an explicit writable path. |

## Moxfield curl fallback

Moxfield imports primarily use the .NET HTTP client against the anonymous deck API. Some
Moxfield edge paths can reject that HTTP fingerprint with `403 Forbidden`; when that
happens and `MtgMcp:Moxfield:EnableCurlFallback` is true, the gateway retries the same URL
through `curl`.

The fallback is intentionally narrow:

- It only runs after a `403 Forbidden` response from the normal HTTP request.
- It uses `ProcessStartInfo.ArgumentList` with `UseShellExecute = false`; request values are
  not concatenated into a shell command.
- It sends `Accept: application/json`, follows redirects, uses the configured User-Agent,
  and sets curl `--max-time 30`.
- It returns to the normal sanitized HTTP error path if curl is missing, exits nonzero,
  returns empty output, or returns malformed JSON.
- Unit tests keep `EnableCurlFallback = false`, so normal `task test` does not require
  network access or a local curl binary.

Configuration:

| Setting | Default | Notes |
|---|---|---|
| `MtgMcp:Moxfield:EnableCurlFallback` / `MOXFIELD:CURL_FALLBACK_ENABLED` | `true` | Disable for hermetic environments or when curl is not permitted. |
| `MtgMcp:Moxfield:CurlPath` / `MOXFIELD:CURL_PATH` | `curl` | Set to an absolute path when PATH is controlled by a service manager. |
| `MtgMcp:Moxfield:UserAgent` / `MOXFIELD:USER_AGENT` | shared default | Also used by the curl retry. |

This is a compatibility workaround, not a provider contract. If Moxfield changes its edge
behavior or anonymous API requirements, the fallback may stop helping and should fail closed
into the existing sanitized error message.
