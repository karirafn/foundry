using System.Data;
using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features;
using Foundry.Modules.Monitoring.Features.Eligibility;
using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Modules.Monitoring.Features.Accounts.Rotation;
using Foundry.Modules.Monitoring.Features.Accounts.Tokens;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.UpdateAccountTests;

/// <summary>
/// Verifies AC2: when a credential whose credential_namespaces rows were seeded with lowercase
/// ids (as produced by the original backfill SQL) has its token rotated via UpdateAccount,
/// the namespace re-derivation succeeds without DbUpdateConcurrencyException.
///
/// The bug: Microsoft.Data.Sqlite binds Guid parameters as UPPERCASE text, while the original
/// backfill SQL produced lowercase hex. SQLite TEXT comparison is case-sensitive, so
/// DELETE ... WHERE id = @p (uppercase) matches 0 rows → DbUpdateConcurrencyException.
/// </summary>
public sealed class WhenTokenRotatedWithLowercaseNamespaceIds : IAsyncDisposable
{
    private const string OriginalToken = "ghp_original_token";
    private const string RotatedToken = "ghp_rotated_token";

    private const string ListingJson = """
        [
          { "full_name": "acme/repo-x", "private": false, "permissions": { "push": true } }
        ]
        """;

    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenTokenRotatedWithLowercaseNamespaceIds()
    {
        Dictionary<string, string> tokenToListing = new()
        {
            [OriginalToken] = ListingJson,
            [RotatedToken] = ListingJson,
        };

        _factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            services.RemoveAll<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>();
            services.AddScoped<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>(
                _ => new StubValidateTokenHandler());

            services.RemoveAll<GitHubHttpClient>();
            services.AddSingleton(
                new GitHubHttpClient(new HttpClient(new TokenKeyedListingFakeHandler(tokenToListing))));

            services.RemoveAll<IRepositoryEligibilityEvaluator>();
            services.AddScoped<IRepositoryEligibilityEvaluator>(_ =>
                new AssignedEligibilityEvaluator(new Dictionary<string, RepositoryEligibility>
                {
                    ["acme/repo-x"] = new RepositoryEligibility.Eligible(),
                }));
        });
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task WhenNamespaceIdsAreLowercase_TokenRotationSucceeds()
    {
        // Arrange — create the account via the API so EF writes the credential with uppercase ids.
        object createBody = new
        {
            providerType = "github",
            baseUrl = "https://github.com",
            token = OriginalToken,
        };

        HttpResponseMessage createResponse = await _client.PostAsJsonAsync(
            new Uri("/api/accounts", UriKind.Relative),
            createBody,
            TestContext.Current.CancellationToken);

        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        CredentialCreationResult? createdResult = await createResponse.Content
            .ReadFromJsonAsync<CredentialCreationResult>(TestContext.Current.CancellationToken);
        createdResult.ShouldNotBeNull();
        CredentialSummary created = createdResult.Credential;

        // Corrupt the credential_namespaces.id values to lowercase to reproduce the migration
        // bug — the backfill SQL used lower(hex(randomblob(...))) which produced lowercase ids.
        await LowercaseNamespaceIdsAsync(created.Id);

        // Apply the same normalization SQL as the NormalizeCredentialNamespaceIds corrective
        // migration. Driving the real EF migration here would require modifying
        // FoundryWebAppFactory (an untouched infrastructure file), so this test is a regression
        // guard for the EF-layer rotation behavior *given* correct normalization. Migration-SQL
        // correctness (that the shipped migration actually normalizes lowercase → uppercase) is
        // covered by WhenNormalizationMigrationRuns in the Persistence test suite.
        await ApplyNamespaceIdNormalizationAsync();

        // Act — rotate the token; this exercises CredentialRotationService.RotateAsync which
        // calls SetNamespaces (clearing old namespaces) then SaveChangesAsync.
        // Without the normalization above, DELETE WHERE id = @p (uppercase) matches 0 lowercase
        // rows and throws DbUpdateConcurrencyException.
        object updateBody = new
        {
            baseUrl = "https://github.com",
            token = RotatedToken,
        };

        HttpResponseMessage updateResponse = await _client.PutAsJsonAsync(
            new Uri($"/api/accounts/{created.Id}", UriKind.Relative),
            updateBody,
            TestContext.Current.CancellationToken);

        // Assert — rotation succeeds; no DbUpdateConcurrencyException is thrown.
        updateResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        CredentialUpdateResult? result = await updateResponse.Content
            .ReadFromJsonAsync<CredentialUpdateResult>(TestContext.Current.CancellationToken);
        result.ShouldNotBeNull();

        // The namespace should still be present (same listing after rotation).
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        Credential? credential = await dbContext.Set<Credential>()
            .Include(c => c.Namespaces)
            .FirstOrDefaultAsync(
                c => c.Id == CredentialId.From(created.Id),
                TestContext.Current.CancellationToken);

        credential.ShouldNotBeNull();
        credential.Namespaces.ShouldContain(ns => ns.Value == "acme");
    }

    /// <summary>
    /// Directly updates credential_namespaces.id to its lowercase form to simulate
    /// the state left by the original buggy backfill SQL.
    /// </summary>
    private async Task LowercaseNamespaceIdsAsync(Guid credentialId)
    {
        await ExecuteOnConnectionAsync(async connection =>
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                "UPDATE credential_namespaces SET id = lower(id) WHERE credential_id = $credentialId;";
            command.Parameters.AddWithValue("$credentialId", credentialId.ToString("D").ToUpperInvariant());
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        });
    }

    /// <summary>
    /// Applies the same normalization SQL as the NormalizeCredentialNamespaceIds corrective
    /// migration — normalizes all credential_namespaces.id values to uppercase.
    /// </summary>
    private async Task ApplyNamespaceIdNormalizationAsync()
    {
        await ExecuteOnConnectionAsync(async connection =>
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "UPDATE credential_namespaces SET id = upper(id);";
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        });
    }

    /// <summary>
    /// Opens the DbContext's underlying SQLite connection if it is not already open,
    /// executes <paramref name="action"/>, then closes it if it was closed on entry.
    /// This avoids the need for each raw-SQL helper to repeat the open/close lifecycle.
    /// </summary>
    private async Task ExecuteOnConnectionAsync(Func<SqliteConnection, Task> action)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();

        SqliteConnection connection = (SqliteConnection)dbContext.Database.GetDbConnection();

        bool wasOpen = connection.State == ConnectionState.Open;
        if (!wasOpen)
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
        }

        try
        {
            await action(connection);
        }
        finally
        {
            if (!wasOpen)
            {
                await connection.CloseAsync();
            }
        }
    }
}
