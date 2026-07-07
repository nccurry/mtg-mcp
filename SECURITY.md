# Security

Report security issues privately to the maintainers.

Do not share Archidekt session tokens, usernames/passwords, Playgroup API keys,
or local credential paths in issues. The released
legacy server redacts known secret fields from config resources and logs, but
callers should still treat MCP transcripts as sensitive when provider
credentials are configured. Rewrite fixtures and live-test evidence must retain
only sanitized contract facts and cleanup outcomes.

The rewrite foundation projects path-free public configuration status and uses
an explicit sensitive-value redactor for diagnostic boundaries. Invalid mode,
path, JSON, and command-line errors return fixed messages rather than rejected
values or local paths. Its stdio host clears default console logging so stdout
contains only JSON-RPC. The current Scryfall adapter requires no credential,
rejects unexpected pagination/download hosts, and returns path-free provider
and storage failures.

The Archidekt adapter reads credentials from explicit configuration or the
standard user-profile credential file, but returns only configured/usable/error
state. Its provider origin is fixed to Archidekt so runtime configuration
cannot redirect credentials to another host. Tokens remain in process memory.
Provider bodies, credential values, account identities, and secret paths are
excluded from public errors and acceptance evidence. Remote mutations require
`remote` mode and exact guarded requests; pull is the only Archidekt operation
permitted to mutate local state in `local` mode.

The Playgroup adapter likewise accepts explicit private configuration or the
standard user-profile `.mtg-mcp/playgroup.json` file. It parses only one
string-valued `apiKey`, fixes the provider origin, and reports credential
readiness without returning the key or file path.
