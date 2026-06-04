using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Issues.Endpoints.RetryEligibilityTests;

public sealed class WhenRetrySucceedsWithBlockedDependencies : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    private static readonly MonitoredRepositoryId RepositoryId = MonitoredRepositoryId.New();

    private static IssueAuthor ValidAuthor =>
        ((Result<IssueAuthor>.Success)IssueAuthor.Create("octocat")).Value;

    private static ProviderUrl ValidUrl =>
        ((Result<ProviderUrl>.Success)ProviderUrl.Create("https://github.com/owner/repo/issues/11")).Value;

    public WhenRetrySucceedsWithBlockedDependencies()
    {
        _factory = new FoundryWebAppFactory(services =>
        {
            services.RemoveAll<IBranchProtectionValidator>();
            services.AddScoped<IBranchProtectionValidator>(_ => new NoViolationsBranchProtectionValidator());
        });
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<IneligibleIssue> SeedIneligibleIssueWithBlockerAsync()
    {
        // No POST endpoint exists for ineligible issues — they are created via domain transitions.
        // Seed directly through DbContext. SetBlockedBy is internal but visible via InternalsVisibleTo.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        DetectedIssue detected = DetectedIssue.Detect(
            RepositoryId,
            issueNumber: 11,
            title: "An ineligible issue with a blocker",
            body: "Issue body text",
            author: ValidAuthor,
            url: ValidUrl,
            labels: ["bug"],
            detectedAt: DateTimeOffset.UtcNow);

        IneligibleIssue ineligible = IneligibleIssue.FromDetected(
            detected,
            [EligibilityViolation.AllowDirectPushes()]);

        // Set a blocker so ClearViolations() transitions to BlockedIssue
        ineligible.SetBlockedBy([99]);

        dbContext.Set<Issue>().Add(ineligible);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return ineligible;
    }

    [Fact]
    public async Task ReturnsIssueInBlockedState()
    {
        // Arrange
        IneligibleIssue issue = await SeedIneligibleIssueWithBlockerAsync();

        // Act
        HttpResponseMessage response = await _client.PostAsync(
            new Uri($"/api/issues/{issue.Id.Value}/retry-eligibility", UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        IssueDetail? detail = await response.Content.ReadFromJsonAsync<IssueDetail>(
            TestContext.Current.CancellationToken);
        detail.ShouldNotBeNull();
        detail.State.ShouldBe("blocked");
    }

    private sealed class NoViolationsBranchProtectionValidator : IBranchProtectionValidator
    {
        public Task<Result<IReadOnlyList<EligibilityViolationInfo>>> ValidateAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(
                Result<IReadOnlyList<EligibilityViolationInfo>>.Ok(
                    (IReadOnlyList<EligibilityViolationInfo>)[]));
        }
    }
}
