using System.Net;

using Foundry.IntegrationTests.Modules.Monitoring.Endpoints.CreateAccountTests;

using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features;
using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Modules.Monitoring.Features.Eligibility;
using Foundry.Shared;

using Microsoft.Extensions.DependencyInjection;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.UpdateAccountTests;

/// <summary>
/// Always returns a valid token response with AccountName "test-user".
/// </summary>
internal sealed class StubValidateTokenHandler : IQueryHandler<ValidateToken.Query, ValidateToken.Response>
{
    public Task<Result<ValidateToken.Response>> HandleAsync(
        ValidateToken.Query query,
        CancellationToken cancellationToken)
    {
        ValidateToken.Response response = new(
            IsValid: true,
            IsAuthFailure: false,
            ScopesVerified: true,
            MissingScopes: [],
            AccountName: "test-user");
        return Task.FromResult(Result<ValidateToken.Response>.Ok(response));
    }
}

/// <summary>
/// Returns the repo listing JSON keyed by the Bearer token in the Authorization header.
/// Returns 422 (Granted) for probe POSTs so that write-permission probing passes.
/// Falls back to "[]" for unknown tokens on listing requests.
/// </summary>
internal sealed class TokenKeyedListingFakeHandler(Dictionary<string, string> tokenToListing)
    : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (StaticListingFakeHandler.IsProbePost(request))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.UnprocessableEntity));
        }

        // Extract the token from Authorization header (GitHub)
        string lastTokenSeen = string.Empty;
        if (request.Headers.TryGetValues("Authorization", out IEnumerable<string>? authValues))
        {
            string bearer = authValues.FirstOrDefault() ?? string.Empty;
            lastTokenSeen = bearer.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? bearer["Bearer ".Length..]
                : string.Empty;
        }

        string json = tokenToListing.TryGetValue(lastTokenSeen, out string? listing)
            ? listing
            : "[]";

        HttpResponseMessage response = new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };
        return Task.FromResult(response);
    }
}

/// <summary>
/// Assigns a fixed eligibility to each repo by slug, then no-ops for unknown slugs.
/// </summary>
internal sealed class AssignedEligibilityEvaluator(
    Dictionary<string, RepositoryEligibility> assignments) : IRepositoryEligibilityEvaluator
{
    public Task EvaluateAndStoreAsync(
        MonitoredRepository repo,
        CancellationToken cancellationToken)
    {
        if (assignments.TryGetValue(repo.Slug.FullPath, out RepositoryEligibility? eligibility))
        {
            repo.SetEligibility(eligibility);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Sets every repo to Unreachable, regardless of namespace coverage.
/// Used for the transient-failure edge case to simulate the real evaluator behaviour
/// when the provider is unavailable.
/// </summary>
internal sealed class UnreachableEligibilityEvaluator : IRepositoryEligibilityEvaluator
{
    public Task EvaluateAndStoreAsync(
        MonitoredRepository repo,
        CancellationToken cancellationToken)
    {
        repo.SetEligibility(new RepositoryEligibility.Unreachable());
        return Task.CompletedTask;
    }
}
