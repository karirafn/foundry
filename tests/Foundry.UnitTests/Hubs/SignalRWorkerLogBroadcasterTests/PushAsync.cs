using Foundry.Modules.Workers.Contracts;
using Foundry.WebApi.Hubs;

using Microsoft.AspNetCore.SignalR;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Hubs.SignalRWorkerLogBroadcasterTests;

public sealed class PushAsync
{
    [Fact]
    public async Task WhenReportProvided_SendsReportReceivedToIssueGroup()
    {
        // Arrange
        Guid issueId = Guid.NewGuid();
        WorkerReportSummary report = new(
            Id: Guid.NewGuid(),
            WorkerRunId: Guid.NewGuid(),
            SequenceNumber: 1,
            ReportType: "progress",
            Content: "Building...",
            IngestedAt: DateTimeOffset.UtcNow);

        SpyHubContext hubContext = new();
        SignalRWorkerLogBroadcaster sut = new(hubContext);

        // Act
        await sut.PushAsync(issueId, report, TestContext.Current.CancellationToken);

        // Assert
        hubContext.SentGroupName.ShouldBe($"issue-{issueId}");
        hubContext.SentMethod.ShouldBe("ReportReceived");
        hubContext.SentArgs.ShouldHaveSingleItem();
        hubContext.SentArgs[0].ShouldBe(report);
    }

    private sealed class SpyHubContext : IHubContext<WorkerLogHub>
    {
        private readonly SpyGroupProxy _groupProxy = new();

        public string? SentGroupName => _groupProxy.SentGroupName;
        public string? SentMethod => _groupProxy.SentMethod;
        public object?[] SentArgs => _groupProxy.SentArgs;

        IHubClients IHubContext<WorkerLogHub>.Clients => new SpyHubClients(_groupProxy);

        IGroupManager IHubContext<WorkerLogHub>.Groups => null!;
    }

    private sealed class SpyHubClients : IHubClients
    {
        private readonly SpyGroupProxy _groupProxy;

        public SpyHubClients(SpyGroupProxy groupProxy)
        {
            _groupProxy = groupProxy;
        }

        public IClientProxy All => null!;
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => null!;
        public IClientProxy Client(string connectionId) => null!;
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => null!;

        public IClientProxy Group(string groupName)
        {
            _groupProxy.SentGroupName = groupName;
            return _groupProxy;
        }

        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => null!;
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => null!;
        public IClientProxy User(string userId) => null!;
        public IClientProxy Users(IReadOnlyList<string> userIds) => null!;
    }

    private sealed class SpyGroupProxy : IClientProxy
    {
        public string? SentGroupName { get; set; }
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
