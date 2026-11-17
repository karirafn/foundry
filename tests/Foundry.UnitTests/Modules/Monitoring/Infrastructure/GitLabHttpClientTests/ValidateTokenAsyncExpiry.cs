using System.Net;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitLab;
using Foundry.Modules.Monitoring.Infrastructure.RateBudget;
using Foundry.Shared;
using Foundry.Testing;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitLabHttpClientTests;

/// <summary>
/// The token expiry read from <c>expires_at</c> on <c>GET /personal_access_tokens/self</c>. The field
/// shape (a <c>yyyy-MM-dd</c> date or <c>null</c>) is from GitLab's Personal access tokens API docs.
/// </summary>
public sealed class ValidateTokenAsyncExpiry
{
    private static readonly Uri ValidBaseUrl = new("https://gitlab.com/api/v4");

    private const string UserJson = """{ "id": 1, "username": "alice" }""";

    private static GitLabHttpClient CreateSut(FakeHandler handler) =>
        new(new HttpClient(handler), NullLogger<GitLabHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))), new InMemoryProviderRateBudget(), TimeProvider.System);

    private static async Task<TokenValidationOutcome> ValidateAsync(FakeHandler handler) =>
        (await CreateSut(handler).ValidateTokenAsync(ValidBaseUrl, "glpat_token", CancellationToken.None)).ValueOrThrow();

    [Fact]
    public async Task WhenExpiresAtIsADate_ReturnsMidnightUtcOnThatDate()
    {
        // Arrange
        FakeHandler handler = new FakeHandler(HttpStatusCode.OK, UserJson)
            .WithRoute("personal_access_tokens/self", HttpStatusCode.OK, """{ "scopes": ["api"], "expires_at": "2026-08-15" }""");

        // Act
        TokenValidationOutcome outcome = await ValidateAsync(handler);

        // Assert
        outcome.ShouldBeOfType<TokenValidationOutcome.AuthenticatedOutcome>().TokenExpiry
            .ShouldBe(new TokenExpiry.On(new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public async Task WhenExpiresAtIsNull_ReturnsNever()
    {
        // Arrange
        FakeHandler handler = new FakeHandler(HttpStatusCode.OK, UserJson)
            .WithRoute("personal_access_tokens/self", HttpStatusCode.OK, """{ "scopes": ["api"], "expires_at": null }""");

        // Act
        TokenValidationOutcome outcome = await ValidateAsync(handler);

        // Assert
        outcome.ShouldBeOfType<TokenValidationOutcome.AuthenticatedOutcome>().TokenExpiry
            .ShouldBe(new TokenExpiry.Never());
    }

    [Theory]
    [InlineData("\"15/08/2026\"")]
    [InlineData("\"2026-08-15T10:00:00Z\"")]
    [InlineData("12345")]
    public async Task WhenExpiresAtIsNotADate_ReturnsUnavailable(string expiresAtJson)
    {
        // Arrange
        FakeHandler handler = new FakeHandler(HttpStatusCode.OK, UserJson)
            .WithRoute("personal_access_tokens/self", HttpStatusCode.OK, $$"""{ "scopes": ["api"], "expires_at": {{expiresAtJson}} }""");

        // Act
        TokenValidationOutcome outcome = await ValidateAsync(handler);

        // Assert
        outcome.ShouldBeOfType<TokenValidationOutcome.AuthenticatedOutcome>().TokenExpiry
            .ShouldBe(new TokenExpiry.Unavailable());
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    public async Task WhenSelfReturnsNonSuccess_ReturnsUnavailable(int statusCode)
    {
        // Arrange
        FakeHandler handler = new FakeHandler(HttpStatusCode.OK, UserJson)
            .WithRoute("personal_access_tokens/self", (HttpStatusCode)statusCode, string.Empty);

        // Act
        TokenValidationOutcome outcome = await ValidateAsync(handler);

        // Assert
        outcome.ShouldBeOfType<TokenValidationOutcome.ScopesUnverifiableOutcome>().TokenExpiry
            .ShouldBe(new TokenExpiry.Unavailable());
    }
}
