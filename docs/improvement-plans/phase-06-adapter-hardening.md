# Phase 6 - Adapter Layer Hardening

| | |
|---|---|
| Effort | L-XL |
| Risk | Medium |
| Depends on | none (parallel with Phase 4/5) |
| Unblocks | Phase 8 (new provider work on a solid base) |
| Target version | 0.11.0 (redaction + JWT, early) and 0.13.0 (broad resiliency); parallel track |

Goal: make the six adapters consistent, resilient, and safe - shared retry/backoff, one
error model, precise secret redaction, and real token refresh - without leaking adapter
concerns into Core.

## 1. Problems addressed

- **P18 - no shared resiliency.** Scryfall/Archidekt hand-roll retry/backoff; Moxfield,
  CommanderSpellbook, and the five Decklists providers have none (a 429 just throws).
- **P19 - inconsistent error model.** Gateways throw redacted `HttpRequestException`;
  corpus providers still have inconsistent failure handling and should return typed,
  degrading statuses where possible.
- **P20 (safety) - coarse secret redaction (addressed by 4.3).** Before the Phase 6
  redaction slice, `SecretRedactor.Redact(string)` replaced the whole value if it merely
  contained a keyword like "token"/"secret"; conversely a raw bearer/JWT without those
  keywords was not redacted. Gateway error bodies still route through the redactor.
- **P21 - Archidekt JWT never refreshes (addressed by 4.4).** Before the Phase 6 JWT
  slice, the token was cached on `DefaultRequestHeaders` for process lifetime; expiry
  could cause write failures until the process restarted.
- **P22 - duplication + divergent caches.** Re-implemented JSON readers, credentials-file
  parsing, `FirstNonEmpty`, rate-limit body parsing; three caches (shared `ICorpusCache`,
  Archidekt's bespoke disk card-id cache, Scryfall trend/meta in-memory `ProviderCache`
  that ignores the configured cache policy); process-static mutable rate-limit state.

## 2. Goals / non-goals

Goals:
- One shared resiliency layer (retry/backoff/timeout) across all adapters.
- One adapter error/result model with graceful degradation; informative + redacted.
- Precise secret redaction as a backstop, plus minimized raw-body exposure in logs/errors
  (do not rely on redaction alone).
- Archidekt JWT expiry detection + re-login.
- Shared helpers (JSON readers, credential parsing, rate-limit parsing); unified caching;
  a shared rate limiter.

Non-goals:
- No new providers (Phase 8) and no Core dependency changes (Core stays package-free; the
  resilience packages live in adapter projects only).

## 3. Current state (investigation)

- Adapters already reference `Microsoft.Extensions.Http` (e.g.
  `MtgMcp.Scryfall.csproj:11`), so `Microsoft.Extensions.Http.Resilience` (Polly-backed)
  drops in naturally at the `AddHttpClient<>` registrations.
- `SecretRedactor` now has precise key-based redaction for dicts/JSON, raw JSON-body
  parsing for string inputs, and token-shape redaction for authorization headers, JWTs,
  URL credentials, and long high-entropy strings. The original coarse substring path has
  been removed.
- Auth flows: Archidekt username/password -> JWT cached with decoded `exp` tracking when
  the login token is JWT-shaped, proactive refresh before expiry, and one re-login/retry
  after a 401; Playgroup API key set per request.
- Caching: `ICorpusCache` (shared, configurable), Archidekt disk card-id cache, and a
  Scryfall `ProviderCache` (in-memory, ignores configured mode/TTLs).
- Rate limiting: Scryfall proactive-by-default (125ms) + 429 handling; Archidekt optional
  sliding window (off by default) + 429/throttle body parsing; both use process-static
  state. Moxfield/Spellbook/Decklists have none.
- Moxfield `curl` fallback on 403 is contained, injection-safe, bounded by curl
  `--max-time 30`, disable-able, and documented in `docs/adapters.md`.

### Completed Phase 6 slices

- **4.3 secret-redaction hardening:** complete. `SecretRedactor.Redact(string)` preserves
  diagnostic prose such as "token expired", structurally redacts raw JSON response bodies,
  and redacts bearer/JWT values, compact JWTs, URL userinfo, and long high-entropy tokens.
  Focused Core tests cover false positives/negatives, and adapter fixture tests cover the
  Archidekt, Moxfield, and Playgroup failed-response consumers.
- **4.4 Archidekt JWT refresh:** complete. The gateway decodes JWT `exp` when present,
  refreshes before an owned session token expires, clears stale tokens before login, and
  retries a failed authenticated request once after a successful re-login on 401.
- **4.6 Moxfield curl fallback documentation:** complete. `docs/adapters.md` documents
  the fallback trigger, external binary dependency, timeout, shell-free argument handling,
  and test isolation.
- **4.7 User-Agent + options consistency:** complete. All adapters now read User-Agent
  settings from options and share `MtgMcpHttpDefaults` for the default value.

## 4. Workstreams

Slice order (do the safety-critical work first, not buried in the broad resiliency
refactor): ship **4.3 secret-redaction hardening** and **4.4 Archidekt JWT refresh** as
their own early PRs (they are the items with security/correctness impact and small blast
radius), then the broader resiliency/error-model/dedup work (4.1, 4.2, 4.5+).

### 4.1 Shared resiliency
- Add `Microsoft.Extensions.Http.Resilience` to the adapter projects; apply a standard
  resilience handler (retry with jittered backoff, timeout, optional circuit breaker) at
  each `AddHttpClient<>` registration via a shared extension
  (e.g. `AddMtgMcpHttpResilience`).
- Keep per-source etiquette (Scryfall pacing, Archidekt throttle awareness) but express it
  through the shared pipeline; bring Moxfield/Spellbook/Decklists up to the same baseline.

### 4.2 Unified error/result model
- Define a shared adapter outcome shape (coordinate with Phase 4 unions and Phase 3 error
  taxonomy): success vs typed failure (auth, rate-limited, unavailable, blocked,
  malformed). Replace bare `EnsureSuccessStatusCode()` paths with consistent handling that
  includes a redacted, useful message.
- Add graceful source-local degradation so a single failing source returns a typed status
  without aborting the run.
- **Cross-phase contract (neither phase blocks the other):** Phase 6 *defines* the typed
  adapter failure shapes; Phase 3 *maps* them to the MCP error taxonomy at the App boundary.
  Phase 6 can land before or in parallel with Phase 3 - until Phase 3 ships, the existing
  exception-to-error behavior remains; once it ships, it consumes these typed shapes.

### 4.3 Harden secret redaction (early, standalone PR)
- Status: complete for the early safety slice; broader raw-body truncation policy can land
  with the unified adapter error model in 4.2.
- Replace/augment `Redact(string)` with precise matching: redact known token shapes (JWT
  `eyJ...`, `Bearer <token>`, long high-entropy strings, URL credentials) and known JSON
  keys, rather than whole-value substring keyword matching.
- **Defense in depth, not redaction alone.** Precise redaction is best-effort and will
  still miss unknown/future secret formats. So the primary control is to *minimize raw-body
  exposure in the first place*: prefer logging status code + a redacted, truncated summary
  over full bodies; include response bodies in errors/logs only when necessary, truncated,
  and only after redaction. Redaction is the backstop, not the guarantee.
- Never apply coarse whole-body replacement; prefer structured redaction. Add tests for
  both false positives (diagnostic body containing "token" stays useful) and false
  negatives (a raw bearer/JWT is redacted even without a keyword).
- Keep redaction in Core (shared), but ensure adapters route all error bodies through it.

### 4.4 Archidekt JWT refresh (early, standalone PR)
- Status: complete for username/password login tokens and 401-triggered re-auth retry.
- Add expiry detection (decode JWT `exp` or track issuance + TTL) and re-login on
  expiry/401; serialize refresh with the existing `authLock`; do a single retry of the
  failed request after refresh. Do not log token contents.

### 4.5 De-duplicate + unify
- Extract shared helpers (a small adapter-support library or Core-adjacent utilities, not
  in Core if it must stay package-free): JSON element readers (`GetString/GetInt/...`),
  credentials-file parsing (JSON or `key=value`), `FirstNonEmpty`, rate-limit `Retry-After`
  / body parsing.
- Unify caching: route the Scryfall trend/meta `ProviderCache` through `ICorpusCache` so it
  honors the configured mode/TTLs; document why the Archidekt card-id cache is separate (it
  is provenance state, not source facts) or fold it in.
- Replace process-static rate-limit state with `System.Threading.RateLimiting` limiters
  registered per host, removing global mutable statics.

### 4.6 Moxfield curl fallback
- Status: complete for the documentation/options slice.
- Keep it (contained, injection-safe, disable-able) but document it in `docs/` as a known
  workaround with its external-binary dependency and fingerprint fragility; ensure it is
  off in tests and bounded by timeout.

### 4.7 User-Agent + options consistency
- Status: complete.
- Centralize the User-Agent string (one source, version-stamped) instead of per-adapter
  drift. Ensure every adapter reads UA from options.

## 5. Files to create / change

- Create: `Directory.Packages.props` (+`Microsoft.Extensions.Http.Resilience`,
  `System.Threading.RateLimiting`), a shared `AddMtgMcpHttpResilience` extension, shared
  adapter-support helpers. `docs/adapters.md` is complete for the 4.6/4.7 slice.
- Change: each adapter's `Add*` registration and request paths; `ArchidektGateway.Auth.cs`
  (refresh); `Core/Options.cs` `SecretRedactor`; Scryfall `ProviderCache` wiring; per-
  adapter UA usage.
- Tests: per-adapter fixture/MockHttp tests for retry/timeout, error taxonomy mapping,
  redaction false-positive/negative cases, and JWT-expiry re-login.

## 6. Testing

- Use `RichardSzalay.MockHttp` (already a dependency) for retry/429/timeout/expiry
  scenarios; keep all `Category!=Live`.
- Redaction unit tests (the safety-critical part) with explicit positive/negative cases.
- Keep `task test` offline; live tests stay `Category=Live`.

## 7. Definition of done

- All adapters share the resilience pipeline; no adapter throws on a transient 429 without
  retry.
- One error model with graceful degradation; `EnsureSuccessStatusCode()`-only paths gone.
- `SecretRedactor` is precise and test-covered for FP/FN, and raw-body exposure in
  logs/errors is minimized (status + redacted, truncated summaries) rather than relying on
  redaction alone.
- Archidekt re-authenticates on expiry.
- Shared helpers replace the duplicated readers/parsers; caches unified or documented;
  rate limiting uses a shared limiter.

## 8. Risks & mitigations

- Risk: resilience changes alter timing/behavior under load. Mitigation: conservative
  defaults, per-source overrides, fixture tests for retry counts.
- Risk: redaction change hides or over-hides data. Mitigation: precise matchers + explicit
  FP/FN tests; never regress the SECURITY.md guarantee.
- Risk: JWT refresh races. Mitigation: reuse the existing single-flight `authLock`; one
  retry only.

## 9. Open questions

- Where to host shared adapter helpers given Core must stay package-free - a new
  `MtgMcp.Adapters.Common` project, or Core utilities that need no packages? (Recommend a
  small shared adapter library that references Core.) **Constraint:** it must hold only
  generic infrastructure (resilience pipeline, JSON-element readers, credential parsing,
  rate-limit parsing). Per `AGENTS.md`, Scryfall/Archidekt own their third-party contracts,
  so provider-specific DTOs/contracts must not migrate into the shared library.
- Add a circuit breaker, or retry+timeout only? (Recommend retry+timeout first; breaker if
  a source proves flaky.)
