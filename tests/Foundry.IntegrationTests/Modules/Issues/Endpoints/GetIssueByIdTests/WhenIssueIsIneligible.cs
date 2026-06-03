using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Issues.Endpoints.GetIssueByIdTests;

public sealed class WhenIssueIsIneligible : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    private static readonly MonitoredRepositoryId RepositoryId = MonitoredRepositoryId.New();

    private static IssueAuthor ValidAuthor =>
        ((Result<IssueAuthor>.Success)IssueAuthor.Create("octocat")).Value;

    private static ProviderUrl ValidUrl =>
        ((Result<ProviderUrl>.Success)ProviderUrl.Create("https://github.com/owner/repo/issues/42")).Value;

    public WhenIssueIsIneligible()
    {
        _factory = new FoundryWebAppFactory();
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<IneligibleIssue> SeedIneligibleIssueAsync()
    {
        // No POST endpoint exists for ineligible issues — they are created via domain transitions.
        // Seed directly through DbContext.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        DetectedIssue detected = DetectedIssue.Detect(
            RepositoryId,
            issueNumber: 42,
            title: "An ineligible issue",
            body: "Issue body text",
            author: ValidAuthor,
            url: ValidUrl,
            labels: ["bug"],
            detectedAt: DateTimeOffset.UtcNow);

        IReadOnlyList<EligibilityViolation> violations =
        [
            EligibilityViolation.AllowDirectPushes(),
            EligibilityViolation.AllowForcePushes()
        ];

        IneligibleIssue ineligible = IneligibleIssue.FromDetected(detected, violations);

        dbContext.Set<Issue>().Add(ineligible);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return ineligible;
    }

    [Fact]
    public async Task ReturnsOkWithStateIneligible()
    {
        // Arrange
        IneligibleIssue issue = await SeedIneligibleIssueAsync();

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri($"/api/issues/{issue.Id.Value}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        IssueDetail? detail = await response.Content.ReadFromJsonAsync<IssueDetail>(
            TestContext.Current.CancellationToken);
        detail.ShouldNotBeNull();
        detail.State.ShouldBe("ineligible");
    }

    [Fact]
    public async Task ReturnsViolationsInStateDetails()
    {
        // Arrange
        IneligibleIssue issue = await SeedIneligibleIssueAsync();

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri($"/api/issues/{issue.Id.Value}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        IssueDetail? detail = await response.Content.ReadFromJsonAsync<IssueDetail>(
            TestContext.Current.CancellationToken);
        detail.ShouldNotBeNull();

        IssueStateDetails stateDetails = detail.StateDetails.ShouldNotBeNull();
        IReadOnlyList<EligibilityViolationDto> violations = stateDetails.Violations.ShouldNotBeNull();
        violations.Count.ShouldBe(2);
        violations.ShouldContain(v => v.Rule == EligibilityViolation.AllowDirectPushes().Rule);
        violations.ShouldContain(v => v.Rule == EligibilityViolation.AllowForcePushes().Rule);
    }
}
