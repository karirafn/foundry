# One declared response schema per endpoint and status code

## Context

`POST /api/accounts` answers 409 with two structurally different bodies: a `NamespaceConflictResponse` carrying the namespace conflicts the caller can resolve through takeover, and a bare string rejecting a duplicate account.
`PUT /api/accounts/{id}` answers 409 with three — a claimed-elsewhere payload plus two bare-string rejections.

ASP.NET Core's OpenAPI generator keeps only the **last** `.Produces` declared for a status code, replacing the whole response object rather than merging content entries.
Verified against `Microsoft.AspNetCore.OpenApi` 10.0.11 (the version pinned in `Directory.Packages.props`) with a throwaway probe project: declaring `.Produces<A>(409)` followed by `.Produces<string>(409)` emitted only the string; `.Produces<A>(409)` followed by `.ProducesProblem(409)` emitted only `ProblemDetails`; and `.Produces<A>(409, "application/json")` followed by `.Produces<B>(409, "application/vnd.b+json")` emitted only `B`.
Distinct media types do **not** coexist under one status code — the escape hatch that reading the OpenAPI spec alone would suggest is closed.

Declaring a second `.Produces` for a status code therefore deletes the first one's schema from `components.schemas` if nothing else references it. During #438 this silently removed `NamespaceConflictResponse` and `NamespaceConflict` from `openapi/v1.json`; the Angular client derives its types from those exact component keys (`account.model.ts`), so the deletion would have broken the frontend build.

## Decision

Every endpoint declares **at most one** response schema per status code. When one status code carries several outcomes, they are unified into a single contract discriminated by an enum, rather than expressed as an OpenAPI `oneOf` union.

For the account endpoints this means one conflict envelope per endpoint — `CreateAccountConflictResponse(Reason, Message, Conflicts)` and `UpdateAccountConflictResponse(Reason, Message)` — each with its own reason enum listing only the outcomes that endpoint can actually reach.
`Message` is always populated so a client that does not recognise a reason still has something to render.
`Conflicts` carries the takeover candidates and is empty for every reason other than a namespace conflict; only the create path needs it, because only there is the conflict recoverable.

The discriminator is a C# enum, not a string. `JsonStringEnumConverter` is registered globally (`Program.cs`), so an enum reaches the spec as a string enum and `openapi-typescript` generates a TypeScript string union — the client's exhaustiveness is then compile-checked instead of hand-maintained.

### Considered options

**An `IOpenApiOperationTransformer` emitting `oneOf`** — rejected. The probe confirmed a transformer can emit `oneOf`, but `context.GetOrCreateSchemaAsync` inlines the schema instead of emitting a `$ref` and does not register the type in `components.schemas`. Forcing `$ref`s by hand produced a spec whose `components.schemas` was missing the referenced type — a dangling reference. `openapi-typescript` does fail loudly on that (`Can't resolve $ref`, non-zero exit), so it would not ship silently, but making the approach correct needs a second document-level transformer that hand-registers each component. Two coordinated transformers and a hand-maintained component list buy a union that the clients do not need, because the discriminated envelope already expresses the same information with a schema `.Produces` can enforce on its own.

**Moving the duplicate-account rejection off 409** — rejected. 409 is the semantically correct status for it, 422 is already taken by takeover validation on the same endpoint, and it would trade a correct status code for doc-generator convenience.

**One conflict contract shared by both endpoints** — rejected. The reachable reason sets differ, so a shared enum would have `POST` advertise `DuplicateNamespace` and `PUT` advertise `NamespaceConflict` plus a permanently empty conflicts list, making states representable that the server cannot produce.

## Consequences

The invariant is mechanically enforced by `.Produces<T>` itself: a second declaration for the same status is the defect, so there is no separate check to remember. The rule is what makes that safe to rely on.

Adding an outcome to an existing status code now means extending that status's contract — a new enum member and, if needed, a new member on the envelope — rather than adding a `.Produces` call. Extending the enum is a wire-compatible change for clients that fall through to `Message` on an unrecognised reason.

CI already catches the failure mode this decision prevents, from two directions: `npm run generate:api` fails on a dangling `$ref`, and `ng build` fails with `TS2339` when a schema a client type indexes disappears. Both were verified against the degraded spec. The residual uncovered case is deleting a schema no client type references, which is inert while the Angular app is the only consumer.

409 now carries a typed envelope on the account endpoints while 400 still returns bare strings elsewhere in the API, so the error surface is temporarily heterogeneous. A separate migration to RFC 9457 `ProblemDetails` covers the remaining bare-string bodies. It will not subsume this decision: `ProducesProblem` emits the untyped `ProblemDetails` schema, whose extension members reach TypeScript as `unknown`, which would lose the typed conflict list the takeover flow depends on.
