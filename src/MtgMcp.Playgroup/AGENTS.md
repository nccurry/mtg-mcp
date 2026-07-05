# Playgroup Adapter Instructions

These instructions apply to changes under `MtgMcp.Playgroup`.

## Provider Boundary

- Use only operations in the pinned official Playgroup public OpenAPI fixture.
  Do not reverse engineer private endpoints or emulate missing deck updates.
- Keep the provider origin fixed. The API key may exist only in private
  configuration, the bearer header, and process memory; never include it,
  account identity, response bodies, or local paths in errors or logs.
- Preserve complete provider JSON inside explicit operation, endpoint, API
  version, retrieval-time, checksum, and limitation evidence. Do not convert
  provider observations into local rankings, quality scores, recommendations,
  or cross-provider hydration.

## Traffic And Writes

- Serialize request starts on the shared non-secret credential lane and retain
  the conservative 250-millisecond minimum unless official guidance requires a
  slower rate.
- Retry only idempotent GET failures within the documented bounds. A `429` may
  replay once only with a present bounded `Retry-After`; `401` and `403` stop.
- Never automatically retry an event batch or live-session creation after any
  response or ambiguous transport failure.
- Keep live tests read-only until the official API documents cleanup for its
  writes. Normal tests must remain offline and fixture-backed.

## Contract Maintenance

- Re-fetch the official OpenAPI document before contract changes. Update its
  observation date, exact byte count, SHA-256, operation inventory, auth/rate
  review, tool schemas, and fixtures together.
- Preserve one MCP tool per documented operation plus the redacted local auth
  status. Keep Playgroup opt-in and both provider writes remote-only.
