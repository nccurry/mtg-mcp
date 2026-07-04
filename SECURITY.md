# Security

Report security issues privately to the maintainers.

Do not share Archidekt session tokens, usernames/passwords, Playgroup API keys,
Tagger cookies/CSRF tokens, or local credential paths in issues. The released
legacy server redacts known secret fields from config resources and logs, but
callers should still treat MCP transcripts as sensitive when provider
credentials are configured. Rewrite fixtures and live-test evidence must retain
only sanitized contract facts and cleanup outcomes.

The rewrite foundation projects path-free public configuration status and uses
an explicit sensitive-value redactor for diagnostic boundaries. Invalid mode,
path, JSON, and command-line errors return fixed messages rather than rejected
values or local paths. Its stdio host clears default console logging so stdout
contains only JSON-RPC; the foundation registers no provider credentials or
remote operation.
