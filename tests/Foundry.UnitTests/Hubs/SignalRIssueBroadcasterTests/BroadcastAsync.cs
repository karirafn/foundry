using Foundry.Modules.Issues.Contracts;
using Foundry.WebApi.Hubs;

using Microsoft.AspNetCore.SignalR;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Hubs.SignalRIssueBroadcasterTests;

public sealed class BroadcastAsync
{
    [Fact]
    public async Task WhenSummaryProvided_SendsIssueUpdatedToAllClients()
    {
        // Arrange
        SpyHubContext hubContext = new();
        SignalRIssueBroadcaster sut = new(hubContext);

        IssueSummary summary = new(
            Id: Guid.NewGuid(),
            IssueNumber: 1,
            Title: "Test Issue",
            State: "detected",
            RepositorySlug: "owner/repo",
            DetectedAt: DateTimeOffset.UtcNow,
            Url: "https://github.com/owner/repo/issues/1",
            FailureClassification: null,
            RepositoryEligibilityStatus: null,
            RunStats: null,
            Stage: "triage",
            NeedsAction: false,
            AwaitsDispatch: true,
            CodeStep: null);

        // Act
        await sut.BroadcastAsync(summary, TestContext.Current.CancellationToken);

        // Assert
        hubContext.SentMethod.ShouldBe("IssueUpdated");
        hubContext.SentArgs.ShouldHaveSingleItem();
        hubContext.SentArgs[0].ShouldBe(summary);
    }

    [Fact]
    public async Task WhenStateChanges_IssueUpdatedPayloadCarriesTheNewPlacement()
    {
        // Arrange
        SpyHubContext hubContext = new();
        SignalRIssueBroadcaster sut = new(hubContext);

        IssueSummary summary = new(
            Id: Guid.NewGuid(),
            IssueNumber: 1,
            Title: "Test Issue",
            State: "review",
            RepositorySlug: "owner/repo",
            DetectedAt: DateTimeOffset.UtcNow,
            Url: "https://github.com/owner/repo/issues/1",
            FailureClassification: null,
            RepositoryEligibilityStatus: null,
            RunStats: null,
            Stage: "awaiting_review",
            NeedsAction: true,
            AwaitsDispatch: false,
            CodeStep: null);

        // Act
        await sut.BroadcastAsync(summary, TestContext.Current.CancellationToken);

        // Assert
        IssueSummary sent = hubContext.SentArgs.ShouldHaveSingleItem().ShouldBeOfType<IssueSummary>();
        sent.ShouldSatisfyAllConditions(
            () => sent.Stage.ShouldBe("awaiting_review"),
            () => sent.NeedsAction.ShouldBeTrue(),
            () => sent.AwaitsDispatch.ShouldBeFalse(),
            () => sent.CodeStep.ShouldBeNull());
    }

    private sealed class SpyHubContext : IHubContext<IssueHub>
    {
        private readonly SpyClientProxy _allProxy = new();

        public string? SentMethod => _allProxy.SentMethod;
        public object?[] SentArgs => _allProxy.SentArgs;

        public IHubClients Clients { get; } = null!;
        public IGroupManager Groups { get; } = null!;

        IHubClients IHubContext<IssueHub>.Clients => new SpyHubClients(_allProxy);

        IGroupManager IHubContext<IssueHub>.Groups => Groups;
    }

    private sealed class SpyHubClients : IHubClients
    {
        private readonly SpyClientProxy _allProxy;

        public SpyHubClients(SpyClientProxy allProxy)
        {
            _allProxy = allProxy;
        }

        public IClientProxy All => _allProxy;
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => _allProxy;
        public IClientProxy Client(string connectionId) => _allProxy;
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => _allProxy;
        public IClientProxy Group(string groupName) => _allProxy;
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => _allProxy;
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => _allProxy;
        public IClientProxy User(string userId) => _allProxy;
        public IClientProxy Users(IReadOnlyList<string> userIds) => _allProxy;
    }

    private sealed class SpyClientProxy : IClientProxy
    {
        public string? SentMethod { get; private set; }
        public object?[] SentArgs { get; private set; } = [];

        public Task SendCoreAsync(
            string method,
            object?[] args,
            CancellationToken cancellationToken = default)
        {
            SentMethod = method;
            SentArgs = args;
            return Task.CompletedTask;
        }
    }
}
