# Serialize Enums as Strings for All HTTP JSON Responses

## Context

`ImageBuildStatus` was serialized as a number (e.g., `2` for `Failed`) in `/api/settings` responses because ASP.NET Core's default `JsonSerializerOptions` does not include `JsonStringEnumConverter`. The Angular dashboard could not render the failed banner because it received a raw integer rather than the expected string `"Failed"`.

## Decision

Register `JsonStringEnumConverter` globally via `ConfigureHttpJsonOptions` in `Program.cs`:

```csharp
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
```

This is a WebApi concern, not shared infrastructure — `ServiceDefaults` is intentionally excluded. EF Core, the outbox serializer, and worker-settings serializers each use independent `JsonSerializerOptions` instances and are unaffected.

Per-enum `[JsonConverter(typeof(JsonStringEnumConverter))]` attributes were rejected: they require every future contracts enum to add the attribute, reintroducing the class of bug this change fixes.

## Consequences

Integration tests that call `ReadFromJsonAsync<T>` on response bodies containing enum properties must pass `FoundryWebAppFactory.JsonOptions` (which includes `JsonStringEnumConverter` and `PropertyNameCaseInsensitive = true`) to deserialize correctly.
