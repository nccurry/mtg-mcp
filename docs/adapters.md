# Adapter Operations

mtg-mcp adapters talk to public or user-configured services with provider-local
contracts. Core owns shared domain models and safe helper defaults; adapter
projects own third-party HTTP request and response shapes.

## Clean-Break `0.9.0` Target

The stable rewrite uses implemented isolated adapters for official Scryfall
evidence, explicit Archidekt operations, and the documented Playgroup public
API. It has no separate Tagger adapter, Moxfield
network adapter, Commander Spellbook adapter, generic decklist provider, or
recommendation source framework.

- Archidekt uses the currently available web API for explicit user-owned deck
  synchronization, folder organization, and named snapshot lifecycle/restore,
  with conservative pacing, preview/apply guards, and verified cleanup.
- Playgroup follows the pinned official OpenAPI contract; missing operations are
  reported unsupported rather than reverse engineered.
- Scryfall explicitly synchronizes official All Cards, Rulings, Oracle Tags,
  and Art Tags bulk files into one corpus. Arbitrary uncached searches remain
  provider-authoritative; no Tagger-site acquisition is planned. API request
  starts share a SQLite-backed 500-millisecond minimum interval, blocking
  responses stop immediately, and transient transport/5xx failures retry at
  most twice.
- Moxfield is manual interchange only because its terms prohibit automated
  access.

See the [rewrite guide](rewrite-guide.md) and the individual provider PLCs for
the reviewed contract. The sections below describe removed legacy adapters as
historical reference only; those projects are not present on this branch.

Current Scryfall configuration uses `SCRYFALL_TTL_HOURS` in `mtg-mcp.json`,
`MTGMCP__SCRYFALL_TTL_HOURS` in the environment, or
`--scryfall-ttl-hours`; it defaults to 24 hours. The product/version User-Agent
and documented JSON Accept header are fixed by the adapter rather than exposed
as arbitrary runtime header input.

Current Archidekt configuration is `ARCHIDEKT:USERNAME`,
`ARCHIDEKT:PASSWORD`, and `ARCHIDEKT:CREDENTIALS_FILE`, with equivalent
`MTGMCP__ARCHIDEKT__...` environment keys. The provider origin is fixed to
Archidekt so configured credentials cannot be redirected to another host. The
adapter retains login tokens only in memory. It starts at most one
request at a time per account in the process, waits at least two seconds
between starts, permits at most 30 starts in a rolling minute, and shares one
150-request budget across every adapter call composed by a tool invocation.
`Retry-After` creates a bounded shared cooldown; `403`/`429` and ambiguous
writes are not retried.

Current Playgroup configuration is `PLAYGROUP:API_KEY`, with the equivalent
`MTGMCP__PLAYGROUP__API_KEY` environment key. Its origin and User-Agent are
fixed. Request starts share a process-wide non-secret credential lane and wait
at least 250 milliseconds. Idempotent GETs have at most two transient retries,
while writes are always single-attempt. A `429` is replayed once only when its
`Retry-After` is present and within the bounded wait; all other throttle cases
stop with a structured unavailable result.

## Historical Legacy Adapter Operations

### User-Agent defaults

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

### Archidekt card-id cache

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

### Moxfield curl fallback

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
