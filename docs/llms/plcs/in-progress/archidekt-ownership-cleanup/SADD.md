# Archidekt Ownership Cleanup Software Architecture And Design Document

## Document Control

- Lifecycle status: In progress
- PLC packet: [README.md](README.md)
- Parent PLC: [Evidence-First Deckbuilding Evolution](../../planned/evidence-first-deckbuilding-evolution/README.md)
- Owner: mtg-mcp
- Last updated: 2026-09-07
- Related requirements: [SRD.md](SRD.md)
- Related implementation plan: [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)
- Implementation authorized: Yes

## Chosen Design

Keep one public ArchidektService facade. It creates one ArchidektSession and
three named transport/operation pairs. The service owns the session lifetime.
Each operation type receives only the transport type it needs and the configured
per-operation request ceiling. Each transport receives only the shared session.

```text
ArchidektService
  ├─ ArchidektSession
  │    ├─ HttpClient / disposal
  │    ├─ credentials and in-memory token
  │    ├─ authentication lock and refresh
  │    ├─ pacing, safe retries, HTTP failure translation
  │    └─ request send / response body
  ├─ ArchidektDeckTransport ─── ArchidektDeckOperations
  ├─ ArchidektFolderTransport ─ ArchidektFolderOperations
  └─ ArchidektSnapshotTransport ─ ArchidektSnapshotOperations
```

This is direct concrete composition inside one adapter project. It adds no
interface, service locator, generic route dispatcher, or cross-provider layer.

## Archidekt Session

ArchidektSession owns only shared provider mechanics:

- the configured or injected HttpClient and its disposal rule;
- validated options and the credential reader;
- the process-local token and login lock;
- the shared request pacer and per-operation budget charging;
- request construction, one authentication refresh, safe read retries,
  rate-limit cooldown, timeout/network handling, and sanitized status mapping;
- the raw response body and received time.

It must not contain a deck, folder, snapshot, card-search, or mutation route.
It must not build a deck, folder, or snapshot payload.

The existing private ProviderResponse record becomes a small internal response
record beside ArchidektSession. It carries the exact body and received time;
the named transport parses and maps that body.

## Provider Routes

| Type | Owns |
| --- | --- |
| ArchidektDeckTransport | Deck listing, deck detail, creation, deletion, metadata/category/card writes, and exact card-ID lookup. It also owns deck-list cursors and deck-format conversion. |
| ArchidektFolderTransport | Folder tree/detail/create routes and folder update, move, and delete writes. |
| ArchidektSnapshotTransport | Named snapshot list/detail/create/update/delete routes. |
| ArchidektProviderId | The one shared numeric-or-opaque provider-ID conversion used by deck and folder payloads. |

Each route calls ArchidektSession directly. A transport maps its own responses
with the existing contract mappers. No catch-all route method or route registry
is allowed.

## Guarded Workflows

| Type | Owns |
| --- | --- |
| ArchidektDeckOperations | Deck reads and creation verification, deletion confirmation and absence check, remote target apply, category/card payloads, plan execution, and deck-list verification. |
| ArchidektFolderOperations | Tree enrichment, parent validation, safe create/update/move/delete workflows, move-item validation, cycle prevention, confirmations, and read-back checks. |
| ArchidektSnapshotOperations | Snapshot reads and writes, confirmation/read-back rules, restore preview, restore apply, and restore-target construction. |
| ArchidektOperationResults | The existing one-to-one conversion from a sanitized ArchidektProviderException into OperationResult. It is adapter-local and contains no route or workflow policy. |

The operation owners create a new ArchidektOperationScope for a standalone
public call and reuse a supplied scope for a composed public call. This keeps
the existing request budget behavior exactly.

## Service Facade

ArchidektService keeps every current public constructor and public method. Its
only responsibilities are:

- validate and retain the configured request ceiling;
- create the session, transports, and operation owners;
- return a new operation scope on request;
- forward stable public calls to the appropriate named operation owner; and
- dispose the session.

The existing internal test construction path may change to construct an
ArchidektSession directly. It is not a public contract. The final code removes
ArchidektOperationContext, ArchidektTransportContext, ArchidektFacade.cs, and
ArchidektTransportFacade.cs.

## Compatibility

The refactor preserves:

- ArchidektService and ArchidektOperationScope public APIs;
- all MCP tool names, schemas, modes, annotations, and toolsets;
- observed provider routes, payloads, headers, token rules, retries, pacing,
  request budgets, timeout behavior, and redacted errors;
- confirmation phrases, fingerprints, read-back verification, typed failures,
  and cancellation behavior;
- project references, packages, configuration, and persisted data.

## File Ownership

| File | Responsibility |
| --- | --- |
| ArchidektService.cs | Public facade and composition root. |
| ArchidektSession.cs | Shared HTTP/authentication/pacing lifecycle. |
| ArchidektDeckTransport.cs | Deck provider routes and deck route helpers. |
| ArchidektFolderTransport.cs | Folder provider routes. |
| ArchidektSnapshotTransport.cs | Snapshot provider routes. |
| ArchidektDeckOperations.cs | Deck safety workflows and apply behavior. |
| ArchidektFolderOperations.cs | Folder safety workflows. |
| ArchidektSnapshotOperations.cs | Snapshot safety workflows. |
| ArchidektProviderId.cs | Shared provider-ID conversion only. |
| ArchidektOperationResults.cs | Shared typed-result conversion only. |

The final tree does not retain a file named Context or a file whose only job is
to forward domain methods to another type.

## Test Design

Keep the existing fake HTTP handler, test options, and deterministic pacer.
Add direct named-owner tests before moving code:

| Area | Required proof |
| --- | --- |
| Session | Authentication retry, rate handling, safe retries, timeout mapping, request count, and redaction remain behaviorally identical. |
| Deck transport and operations | Deck route bytes, create/read-back, deletion verification, apply ordering, card lookup, request budgets, and typed conflicts remain unchanged. |
| Folder transport and operations | Tree reads, parent rules, move/cycle rules, updates, deletion safeguards, and read-back remain unchanged. |
| Snapshot transport and operations | List/get/create/update/delete, restore preview/apply, fingerprint checks, and confirmation behavior remain unchanged. |
| Ownership | Reflection confirms both retired Context types are absent after the move. |

Existing service tests remain the public-behavior proof. Direct named-owner
tests prove the new physical boundary without live traffic.

The session tests must also prove owned-client disposal and borrowed-client
preservation. The ownership test must confirm that ArchidektSession has no
deck, folder, or snapshot route. It may inspect only those narrow boundaries;
behavior tests remain the main safety proof.

## Failure Handling

ArchidektSession continues to throw only the existing sanitized
ArchidektProviderException classifications for provider failures. At an
operation boundary, ArchidektOperationResults maps those classifications to the
same OperationResult case and reason code. No raw response, cookie, token,
credential, request URL containing secrets, or local path may escape.

## Alternatives Rejected

| Alternative | Why it is rejected |
| --- | --- |
| Keep the Context types and improve their summaries. | Names would still hide where real behavior lives. |
| Add interfaces for every route or operation. | This introduces mock-friendly ceremony without removing a real dependency. Fake HTTP already gives useful tests. |
| Keep ArchidektTransport as a forwarding facade. | A second forwarding layer would recreate the same ownership problem. |
| Put all three domain routes in the shared session. | It would turn the session back into a large provider god object. |
| Change provider behavior while moving code. | It would make regressions difficult to isolate. |

## Deferred Work

- New provider routes or account capabilities.
- SDK/package changes.
- New MCP tools or toolset changes.
- Any live-provider contract refresh.

Those need separate children and are not implementation authority for this one.
