# Error code carried in the ProblemDetails type URI

## Context

47 of the API's 53 `.ProducesProblem(...)` declarations describe a body the endpoint never sends: 21 return a bare JSON string as `application/json`, 20 return no body at all, and 6 declare a 500 no code path can produce. Only 6 declarations match a real `TypedResults.Problem(...)`.

Migrating these to RFC 9457 `ProblemDetails` raises a question the bare-string channel never had to answer: every failure already carries an `Error(Code, Message)` — `Repository.DuplicateSlug`, `Issue.WrongState` — and the code is discarded at the HTTP boundary. RFC 9457 offers two places to put it back: the `type` URI or an extension member.

[ADR 0056](0056-one-response-schema-per-endpoint-status.md) settled how many schemas a status may declare. This settles what the error schema contains and what identifies it.

## Decision

Error responses are RFC 9457 `ProblemDetails`. `Error.Code` is carried in the **`type` URI**, appended verbatim to a `tag:` prefix:

```
tag:foundry,2026:problems/Repository.DuplicateSlug
```

`Error.Message` becomes `detail`. `title` is left unset, so ASP.NET fills the status reason phrase.

The mapping is mechanical — the URI is the prefix plus `Error.Code`, with no lookup table in either direction and no code-to-URI registry to keep in step. A shared `Error.ToProblem(int statusCode)` extension in `Foundry.Shared.Infrastructure/Http/` performs it; every module already carries `FrameworkReference Microsoft.AspNetCore.App` and references that project.

The **status code stays an argument at the call site**, not a property of the error. The same code can warrant different statuses at different endpoints, which makes status an endpoint routing decision rather than an error one.

Endpoints narrow their `Results<...>` union to carry `ProblemHttpResult` as the only error arm, which makes a bare-string return a compile error.

### Considered options

**`Error.Code` as an extension member** — rejected, and the reason is not visible from the code. `ProducesProblem` emits the framework's `ProblemDetails` schema, which has no `additionalProperties` (`src/Foundry.WebApi/openapi/v1.json`, `components.schemas.ProblemDetails`). `openapi-typescript` therefore emits a **closed** object type (`src/foundry-web/src/app/api/schema.ts`, the `ProblemDetails` member), so an extension member does not reach TypeScript as `unknown` — it does not appear at all, and reading it is a `TS2339` error. Every client site would need a cast that re-opens the shape the generated types exist to close. `type` is the one machine-readable field the generated type does expose, and RFC 9457 §3.1.1 makes it the designated one: *"Consumers MUST use the `type` URI (after resolution, if necessary) as the problem type's primary identifier."*

**An `https` URI on a domain the project does not own** (`https://foundry.local/problems/...`) — rejected. It matches the RFC's own examples and looks conventional, but every value is a link that 404s for whoever tries to open it. RFC 9457 §3.1.1 permits non-resolvable type URIs and names tag URIs (RFC 4151) as the example, and a `tag:` URI makes its non-resolvability visible instead of promising a document that does not exist.

**An `https` URI into the repository** — rejected. Genuinely resolvable, satisfying §3.1.1's encouragement toward resolvable URIs, but it bakes a hosting provider and a branch name into the wire format and owes a documentation page per error code.

**Deriving `title` from `Error.Code`** — rejected. `type` already carries type identity and is what consumers branch on; `title` is advisory prose. A camel-split of the code yields strings ("Conflict on create") that read worse than the `detail` beside them, and reintroduces a string transform this decision otherwise avoids.

**A code-to-status map inside the helper** — rejected on the same grounds as the lookup table above, and because it would take a routing decision away from the endpoint that owns it.

## Consequences

An explicit `type` survives `ProblemDetailsDefaults`, which otherwise fills a null `type` with the RFC 9110 status-section URI. Verified against `Microsoft.AspNetCore.OpenApi` 10.0.12 (the version pinned in `Directory.Packages.props`) with a throwaway probe: `TypedResults.Problem(detail:, statusCode: 409, type: "tag:...")` serialises as `{"type":"tag:...","title":"Conflict","status":409,"detail":"..."}` with `Content-Type: application/problem+json`. The 6 endpoints that already return a real `ProblemDetails` currently send the status-generic RFC 9110 URI, which this convention replaces with a code-specific one.

**The declaration side stays unenforced.** The same probe showed that `Results<Created<T>, ProblemHttpResult>` with no `.Produces*` calls emits only the 201 — `ProblemHttpResult` contributes nothing to the generated document. So `.ProducesProblem(N)` remains hand-written and unchecked against what the endpoint returns, which is the hole that produced all 47 mislabels. Union narrowing closes the return side at compile time; the declaration side needs a reflection test over `EndpointDataSource` asserting that any endpoint declaring `.ProducesProblem(N)` has a return-type union whose error arms are exclusively `ProblemHttpResult`. That test cannot go green until every site is migrated, so it lands with the migration rather than with this decision. Three endpoints return a bare `IResult` and are opaque to it.

Extending the enum of reachable codes is wire-compatible: a client that does not recognise a `type` still has `detail` to render, per RFC 9457 §3.2, which requires consumers to ignore unrecognised extensions and leaves `detail` always populated.

`AddProblemDetails()` is not registered. Registering it — together with `UseStatusCodePages()` — would give every empty-body error response a conforming `ProblemDetails` without touching the endpoint, but the generated body carries no `detail` and a status-generic `type`, so it produces conformance without preserving `Error.Code`. It also injects a `traceId` extension member, which by the reasoning above is invisible to the generated client types. It is a complement to this decision for the empty-body responses, not a substitute for it.
