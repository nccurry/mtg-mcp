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

- **P18 - shared resiliency convention.** Scryfall/Archidekt retain robust
  adapter-specific retry/backoff because they have source-specific pacing and mutation
  semantics. CommanderSpellbook and Decklists share a package-free text-response retry
  helper. Moxfield/Playgroup intentionally keep adapter-local request loops because of
  Moxfield's curl fallback and Playgroup's per-request auth, but share the same
  redacted/truncated failure handling.
- **P19 - consistent failure handling.** Bare `EnsureSuccessStatusCode` paths are gone,
  adapter HTTP failures now use one redacted/truncated `HttpRequestException` factory,
  and corpus aggregators degrade source-locally when optional sources fail. Source-status
  notes redact exception messages before surfacing them. The remaining design question is
  whether Phase 3/4 should promote these failures into richer typed outcomes rather than
  the current exception-to-status mapping.
- **P20 (safety) - coarse secret redaction (addressed by 4.3).** Before the Phase 6
  redaction slice, `SecretRedactor.Redact(string)` replaced the whole value if it merely
  contained a keyword like "token"/"secret"; conversely a raw bearer/JWT without those
  keywords was not redacted. Gateway error bodies still route through the redactor.
- **P21 - Archidekt JWT never refreshes (addressed by 4.4).** Before the Phase 6 JWT
  slice, the token was cached on `DefaultRequestHeaders` for process lifetime; expiry
  could cause write failures until the process restarted.
- **P22 - duplication + divergent caches.** Scryfall trend/meta now route through the
  shared `ICorpusCache`; Archidekt's adapter-local card-id cache is documented as mutation
  support state; Scryfall and Archidekt request pacing now use host-owned pacers instead
  of adapter process-static mutable state.

## 2. Goals / non-goals

Goals:
- One shared resiliency convention (retry/backoff/timeout where useful) across all
  adapters.
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

- Adapters already reference `Microsoft.Extensions.Http` where they use named clients, so
  `Microsoft.Extensions.Http.Resilience` (Polly-backed) remains a viable later option at
  the `AddHttpClient<>` registrations. Phase 6 intentionally took a smaller package-free
  route because the concrete gaps were retry-delay parsing, bare success checks, redacted
  failures, token refresh, cache ownership, and host-owned pacing.
- `SecretRedactor` now has precise key-based redaction for dicts/JSON, raw JSON-body
  parsing for string inputs, and token-shape redaction for authorization headers, JWTs,
  URL credentials, and long high-entropy strings. The original coarse substring path has
  been removed.
- Auth flows: Archidekt username/password -> JWT cached with decoded `exp` tracking when
  the login token is JWT-shaped, proactive refresh before expiry, and one re-login/retry
  after a 401; Playgroup API key set per request.
- Caching: `ICorpusCache` is shared/configurable and now covers Scryfall search metadata,
  corpus signals, and Scryfall trend/meta facts. Archidekt's separate disk card-id cache
  is documented as mutation support state rather than recommendation source facts.
- Rate limiting: Scryfall proactive-by-default (125ms) + 429 handling; Archidekt optional
  sliding window (off by default) + 429/throttle body parsing; both use host-owned
  `MtgMcpRequestPacer` instances instead of adapter static state. Retry-After and
  body-marker delay parsing now share `MtgMcpHttpRetry`. CommanderSpellbook and
  Decklists use `MtgMcpHttpRetry.SendForStringAsync` for text/json request retry and
  redacted terminal failures. Moxfield/Playgroup keep adapter-local request handling with
  shared redacted terminal failures.
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
- **4.5 rate-limit parser dedupe:** complete for Scryfall/Archidekt retry-delay parsing.
  `MtgMcpHttpRetry` centralizes Retry-After header parsing, provider body-marker parsing,
  and negative-delay clamping. Archidekt cache disposition and proactive limiter
  replacement are complete.
- **4.5 common text helper dedupe:** complete. `MtgMcpText.FirstNonEmpty` replaces the
  repeated local implementations in Core services and adapter mapping/auth paths.
- **4.5 JSON reader dedupe:** complete. `MtgMcpJson` centralizes common `JsonElement`
  string, numeric, boolean, nested-object, collection-envelope, and string-array readers
  while preserving strict numeric parsing for Scryfall and tolerant numeric-string parsing
  for Archidekt, Moxfield, and Playgroup.
- **4.5 credentials-file parser dedupe:** complete. `MtgMcpCredentialsFile` centralizes
  safe file reading, JSON-vs-key-value detection, redacted parse errors, comment skipping,
  and line diagnostics while leaving provider-specific key policy in Archidekt and
  Playgroup.
- **4.5 Scryfall trend/meta cache unification:** complete. The optional-context providers
  now use `ICorpusCache` with the configured Scryfall search TTL, preserve clone isolation,
  and honor cache-off mode via `NullCorpusCache`.
- **4.5 Archidekt card-id cache disposition:** complete. `docs/adapters.md` and the README
  document why this cache stays adapter-local: it stores mutation support state for
  Archidekt-specific card ids, upgrades legacy entries, and evicts stale ids on mutation
  rejection.
- **4.5 limiter replacement:** complete for the existing proactive pacing paths. Scryfall
  and Archidekt now use adapter-specific singleton `MtgMcpRequestPacer` registrations, so
  pacing state is host-owned rather than process-static.
- **4.1/4.2 shared text-response retry and redacted failures:** complete for
  CommanderSpellbook and Decklists. `MtgMcpHttpRetry.SendForStringAsync` retries
  transient statuses with `Retry-After`, returns explicitly allowed non-success statuses
  for graceful missing-page paths, and throws redacted `HttpRequestException` failures.
  The repo no longer has bare `EnsureSuccessStatusCode()` adapter/corpus paths.
- **4.2 shared adapter HTTP exception factory:** complete for Scryfall, Archidekt,
  Moxfield, Playgroup, CommanderSpellbook, and Decklists. Adapter HTTP error bodies now
  pass through `MtgMcpHttpRetry.CreateRequestException`, which redacts, truncates, and
  preserves `HttpRequestException.StatusCode`; Moxfield keeps source-specific diagnostic
  hints via the shared factory.
- **4.2 source-local degradation hygiene:** complete for corpus recommendation and
  commander evidence aggregation. Optional source failures already degraded into
  `CorpusSourceStatusKind.Failed` without aborting the run; those status notes now use
  `SecretRedactor` before surfacing exception messages.
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
- Status: complete for Phase 6 scope. `MtgMcpHttpRetry.SendForStringAsync` is the shared
  baseline for text/json corpus requests that benefit from generic transient retry,
  covering CommanderSpellbook and Decklists with `Retry-After` and redacted terminal
  failures. Scryfall/Archidekt keep source-specific pacing and retry. Moxfield/Playgroup
  keep adapter-local loops for curl fallback/auth but share terminal failure handling.
- Deferred: `Microsoft.Extensions.Http.Resilience` remains optional future work if a
  release needs registration-time timeout or circuit-breaker policy. It is not required
  for the current architecture.

### 4.2 Unified error/result model
- Status: complete for Phase 6 scope. Bare `EnsureSuccessStatusCode()` paths have been replaced,
  and adapter HTTP failures share the same redacted/truncated `HttpRequestException`
  factory. Corpus aggregators already provide source-local degradation and redacted
  failure notes.
- Deferred: decide with Phase 3/4 whether the current exception-to-status mapping should
  become an explicit typed adapter outcome shape: success vs typed failure (auth,
  rate-limited, unavailable, blocked, malformed).
- **Cross-phase contract (neither phase blocks the other):** Phase 6 leaves the App
  boundary on the existing exception-to-error behavior while making those exceptions and
  source statuses safe. Phase 3/4 can introduce richer typed failure shapes later without
  blocking the Phase 6 safety work.

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
- Status: rate-limit retry-delay parsing, JSON readers, credentials-file parsing,
  `FirstNonEmpty`, Scryfall trend/meta cache unification, and Archidekt card-id cache
  disposition are complete; proactive Scryfall/Archidekt limiter replacement is complete.
- Extract shared helpers (a small adapter-support library or Core-adjacent utilities, not
  in Core if it must stay package-free): JSON element readers, credentials-file parsing
  (JSON or `key=value`), `FirstNonEmpty`, and rate-limit `Retry-After` / body parsing are
  now shared Core utilities with no adapter package dependencies.
- Unify caching: Scryfall trend/meta now route through `ICorpusCache` and honor the
  configured mode/TTLs; Archidekt's card-id cache is documented as adapter-local mutation
  support state, not source facts.
- Replace process-static rate-limit state with host-registered `MtgMcpRequestPacer`
  instances, removing global mutable statics. Revisit `System.Threading.RateLimiting`
  during the broader HTTP resilience pipeline if richer policies become useful.

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

- Created/changed: `Core/HttpRetry.cs` now contains the shared retry-delay parsing,
  text-response send helper, and redacted request-exception factory. `docs/adapters.md`
  is complete for the 4.6/4.7 and Archidekt cache-disposition slices.
- Deferred if richer policies become useful: `Directory.Packages.props`
  (`Microsoft.Extensions.Http.Resilience`) and a shared `AddMtgMcpHttpResilience`
  extension in adapter registration code.
- Change: each adapter's `Add*` registration and request paths; `ArchidektGateway.Auth.cs`
  (refresh); `Core/Options.cs` `SecretRedactor`; Scryfall optional-context cache wiring;
  per-adapter UA usage.
- Tests: shared HTTP retry tests, per-adapter fixture/MockHttp tests for retry/timeout,
  error taxonomy mapping, redaction false-positive/negative cases, and JWT-expiry
  re-login.

## 6. Testing

- Use `RichardSzalay.MockHttp` (already a dependency) for retry/429/timeout/expiry
  scenarios; keep all `Category!=Live`.
- Redaction unit tests (the safety-critical part) with explicit positive/negative cases.
- Keep `task test` offline; live tests stay `Category=Live`.

## 7. Definition of done

- Existing text/json corpus request paths share the retry helper; custom adapter request
  paths have explicit dispositions and share terminal failure handling.
- One safe failure convention with graceful degradation; `EnsureSuccessStatusCode()`-only
  paths are gone.
- `SecretRedactor` is precise and test-covered for FP/FN, and raw-body exposure in
  logs/errors is minimized (status + redacted, truncated summaries) rather than relying on
  redaction alone.
- Archidekt re-authenticates on expiry.
- Shared helpers replace the duplicated readers/parsers; caches are unified or documented;
  existing proactive pacing uses host-owned limiter state.

## 8. Risks & mitigations

- Risk: resilience changes alter timing/behavior under load. Mitigation: conservative
  defaults, per-source overrides, fixture tests for retry counts.
- Risk: redaction change hides or over-hides data. Mitigation: precise matchers + explicit
  FP/FN tests; never regress the SECURITY.md guarantee.
- Risk: JWT refresh races. Mitigation: reuse the existing single-flight `authLock`; one
  retry only.

## 9. Open questions

- Where to host package-backed adapter helpers if `Microsoft.Extensions.Http.Resilience`
  lands later? The no-package utilities now live in Core; any package-backed resilience
  extension should live in an adapter-support project or in adapter projects, not in Core.
  **Constraint:** it must hold only generic infrastructure. Per `AGENTS.md`,
  Scryfall/Archidekt own their third-party contracts, so provider-specific DTOs/contracts
  must not migrate into shared infrastructure.
- Add a circuit breaker, or retry+timeout only? (Recommend retry+timeout first; breaker if
  a source proves flaky.)
