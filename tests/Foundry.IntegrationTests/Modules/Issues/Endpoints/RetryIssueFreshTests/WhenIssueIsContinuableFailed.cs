using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Commands;
using Foundry.Shared;
using Foundry.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Issues.Endpoints.RetryIssueFreshTests;

public sealed class WhenIssueIsContinuableFailed : IAsyncDisposable
{
    private const string WorkBranch = "feat/7-wrong-direction";

    private readonly RecordingWorkBranchCommands _branchCommands = new();
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenIssueIsContinuableFailed()
    {
        _factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            services.RemoveAll<IWorkBranchCommands>();
            services.AddSingleton<IWorkBranchCommands>(_branchCommands);
        });
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<ContinuableFailedIssue> SeedContinuableFailedIssueAsync()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        ContinuableFailedIssue failed = new IssueBuilder()
            .WithMonitoredRepositoryId(MonitoredRepositoryId.New())
            .WithIssueNumber(7)
            .WithTitle("Wrong direction")
            .WithBranchName(WorkBranch)
            .ContinuableFailed();

        dbContext.Set<Issue>().Add(failed);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return failed;
    }

    private Task<HttpResponseMessage> PostRetryFreshAsync(IssueId id) =>
        _client.PostAsync(
            new Uri($"/api/issues/{id.Value}/retry-fresh", UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);

    private async Task<Issue?> LoadAsync(IssueId id)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        return await dbContext.Set<Issue>()
            .FirstOrDefaultAsync(i => i.Id == id, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DeletesTheBranchAndRequeuesTheIssueFresh()
    {
        // Arrange
        ContinuableFailedIssue failed = await SeedContinuableFailedIssueAsync();

        // Act
        HttpResponseMessage response = await PostRetryFreshAsync(failed.Id);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        IssueDetail? detail = await response.Content.ReadFromJsonAsync<IssueDetail>(
            TestContext.Current.CancellationToken);
        detail.ShouldNotBeNull().State.ShouldBe("queued");
        _branchCommands.Deleted.ShouldHaveSingleItem().ShouldBe(WorkBranch);
        FreshQueuedIssue queued = (await LoadAsync(failed.Id)).ShouldBeOfType<FreshQueuedIssue>();
        queued.Context.ShouldBeOfType<DispatchContext.Fresh>();
    }

    [Fact]
    public async Task WhenBranchDeletionFails_Returns502AndKeepsTheIssueContinuableFailed()
    {
        // Arrange
        _branchCommands.Result = Result<bool>.Fail(new Error("GitHub.UnexpectedStatusCode", "GitHub API returned unexpected status code 403."));
        ContinuableFailedIssue failed = await SeedContinuableFailedIssueAsync();

        // Act
        HttpResponseMessage response = await PostRetryFreshAsync(failed.Id);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);
        (await LoadAsync(failed.Id)).ShouldBeOfType<ContinuableFailedIssue>();
    }

    [Fact]
    public async Task WhenIssueIsNotContinuableFailed_Returns409()
    {
        // Arrange
        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
            FailedIssue plainFailed = new IssueBuilder().WithIssueNumber(8).Failed();
            dbContext.Set<Issue>().Add(plainFailed);
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

            // Act
            HttpResponseMessage response = await PostRetryFreshAsync(plainFailed.Id);

            // Assert
            response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        }

        _branchCommands.Deleted.ShouldBeEmpty();
    }

    private sealed class RecordingWorkBranchCommands : IWorkBranchCommands
    {
        public List<string> Deleted { get; } = [];

        public Result<bool> Result { get; set; } = Result<bool>.Ok(true);

        public Task<Result<bool>> DeleteBranchAsync(
            MonitoredRepositoryId repositoryId,
            string branchName,
            CancellationToken cancellationToken)
        {
            Deleted.Add(branchName);
            return Task.FromResult(Result);
        }
    }
}
