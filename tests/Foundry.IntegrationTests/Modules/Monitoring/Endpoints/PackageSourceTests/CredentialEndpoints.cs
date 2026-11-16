using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Features.PackageSources;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.PackageSourceTests;

public sealed class CredentialEndpoints : IAsyncDisposable
{
    private const string Token = "registered-token-value";

    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public CredentialEndpoints()
    {
        _factory = new FoundryWebAppFactory();
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private static Uri CredentialUri(Guid id) => new($"/api/package-sources/{id}/credential", UriKind.Relative);

    private static readonly Uri ListUri = new("/api/package-sources", UriKind.Relative);

    private async Task<JsonElement> ListAsync()
    {
        HttpResponseMessage response = await _client.GetAsync(ListUri, TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Get_ListsADeclaredSourceWithItsRepositoriesAndStatus()
    {
        // Arrange
        PackageSourceSeeder.Seeded seeded = await PackageSourceSeeder.SeedAsync(_factory);

        // Act
        JsonElement list = await ListAsync();

        // Assert
        JsonElement source = list.EnumerateArray().ShouldHaveSingleItem();
        source.ShouldSatisfyAllConditions(
            () => source.GetProperty("id").GetGuid().ShouldBe(seeded.SourceId),
            () => source.GetProperty("url").GetString().ShouldBe(PackageSourceSeeder.SourceUrl),
            () => source.GetProperty("repositories").EnumerateArray().ShouldHaveSingleItem()
                .GetProperty("slug").GetString().ShouldBe(PackageSourceSeeder.Slug),
            () => source.GetProperty("repositories")[0].GetProperty("id").GetGuid().ShouldBe(seeded.RepositoryId),
            () => source.GetProperty("status").GetProperty("kind").GetString().ShouldBe("reachableViaAccount"),
            () => source.GetProperty("status").GetProperty("accountName").GetString().ShouldBe(PackageSourceSeeder.AccountName),
            () => source.GetProperty("status").GetProperty("host").GetString().ShouldBe("gitlab.com"),
            () => source.GetProperty("lastCheckedAt").GetDateTimeOffset().ShouldBe(PackageSourceSeeder.CheckedAt),
            () => source.GetProperty("credential").ValueKind.ShouldBe(JsonValueKind.Null));
    }

    [Fact]
    public async Task Put_StoresTheCredentialAndNeverReturnsTheToken()
    {
        // Arrange
        PackageSourceSeeder.Seeded seeded = await PackageSourceSeeder.SeedAsync(_factory);

        // Act
        HttpResponseMessage put = await _client.PutAsJsonAsync(
            CredentialUri(seeded.SourceId),
            new { username = "ci-reader", token = Token },
            TestContext.Current.CancellationToken);

        // Assert
        put.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await put.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldNotContain(Token);

        HttpResponseMessage get = await _client.GetAsync(ListUri, TestContext.Current.CancellationToken);
        string body = await get.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldNotContain(Token);
        JsonElement source = JsonDocument.Parse(body).RootElement.EnumerateArray().ShouldHaveSingleItem();
        source.GetProperty("credential").GetProperty("username").GetString().ShouldBe("ci-reader");
        source.GetProperty("credential").TryGetProperty("token", out _).ShouldBeFalse();
        source.GetProperty("status").GetProperty("kind").GetString().ShouldBe("unchecked");
    }

    [Fact]
    public async Task Put_RequestsAProbeOfTheSource()
    {
        // Arrange
        PackageSourceSeeder.Seeded seeded = await PackageSourceSeeder.SeedAsync(_factory);

        // Act
        await _client.PutAsJsonAsync(
            CredentialUri(seeded.SourceId),
            new { username = "ci-reader", token = Token },
            TestContext.Current.CancellationToken);

        // Assert
        PackageSourceProbeRequests requests = _factory.Services.GetRequiredService<PackageSourceProbeRequests>();
        requests.Reader.TryRead(out PackageSourceId requested).ShouldBeTrue();
        requested.Value.ShouldBe(seeded.SourceId);
    }

    [Fact]
    public async Task Put_WithMissingFields_ReturnsErrorsKeyedByField()
    {
        // Arrange
        PackageSourceSeeder.Seeded seeded = await PackageSourceSeeder.SeedAsync(_factory);

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            CredentialUri(seeded.SourceId),
            new { username = "", token = (string?)null },
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        ValidationProblemDetails problem = (await response.Content
            .ReadFromJsonAsync<ValidationProblemDetails>(TestContext.Current.CancellationToken)).ShouldNotBeNull();
        problem.Errors.ShouldSatisfyAllConditions(
            errors => errors["username"].ShouldBe(["Username is required."]),
            errors => errors["token"].ShouldBe(["Token is required."]));
    }

    [Fact]
    public async Task Put_WithFieldsTooLong_ReturnsErrorsKeyedByField()
    {
        // Arrange
        PackageSourceSeeder.Seeded seeded = await PackageSourceSeeder.SeedAsync(_factory);

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            CredentialUri(seeded.SourceId),
            new { username = new string('u', 256), token = new string('t', 2049) },
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        ValidationProblemDetails problem = (await response.Content
            .ReadFromJsonAsync<ValidationProblemDetails>(TestContext.Current.CancellationToken)).ShouldNotBeNull();
        problem.Errors.ShouldSatisfyAllConditions(
            errors => errors["username"].ShouldBe(["Username must be 255 characters or fewer."]),
            errors => errors["token"].ShouldBe(["Token must be 2048 characters or fewer."]));
    }

    [Fact]
    public async Task Put_ForAnUnknownSource_ReturnsNotFound()
    {
        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            CredentialUri(Guid.NewGuid()),
            new { username = "ci-reader", token = Token },
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken))
            .ShouldNotBeNull().Type.ShouldEndWith(PackageSourceCredentialCommands.NotFoundCode);
    }

    [Fact]
    public async Task Delete_RemovesTheCredentialAndRequestsAProbe()
    {
        // Arrange
        PackageSourceSeeder.Seeded seeded = await PackageSourceSeeder.SeedAsync(_factory);
        await _client.PutAsJsonAsync(
            CredentialUri(seeded.SourceId),
            new { username = "ci-reader", token = Token },
            TestContext.Current.CancellationToken);
        PackageSourceProbeRequests requests = _factory.Services.GetRequiredService<PackageSourceProbeRequests>();
        requests.Reader.TryRead(out _);

        // Act
        HttpResponseMessage response = await _client.DeleteAsync(
            CredentialUri(seeded.SourceId),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        JsonElement source = (await ListAsync()).EnumerateArray().ShouldHaveSingleItem();
        source.ShouldSatisfyAllConditions(
            () => source.GetProperty("credential").ValueKind.ShouldBe(JsonValueKind.Null),
            () => source.GetProperty("status").GetProperty("kind").GetString().ShouldBe("reachableViaAccount"));
        requests.Reader.TryRead(out PackageSourceId requested).ShouldBeTrue();
        requested.Value.ShouldBe(seeded.SourceId);
    }

    [Fact]
    public async Task Delete_ForAnUnknownSource_ReturnsNotFound()
    {
        // Act
        HttpResponseMessage response = await _client.DeleteAsync(
            CredentialUri(Guid.NewGuid()),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
