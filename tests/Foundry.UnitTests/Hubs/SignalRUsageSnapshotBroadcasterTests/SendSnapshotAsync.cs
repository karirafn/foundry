using Foundry.Modules.Credentials.Contracts;
using Foundry.Modules.Credentials.Features.Usage;
using Foundry.Testing;
using Foundry.WebApi.Hubs;

using Microsoft.AspNetCore.SignalR;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Hubs.SignalRUsageSnapshotBroadcasterTests;

public sealed class SendSnapshotAsync
{
    [Fact]
    public async Task WhenSnapshotProvided_SendsUsageSnapshotUpdatedWithMappedDto()
    {
        // Arrange
        SpyHubContext hubContext = new();
        SignalRUsageSnapshotBroadcaster sut = new(hubContext);

        UsageSnapshot snapshot = new UsageSnapshotBuilder().Build();

        // Act
        await sut.SendSnapshotAsync(snapshot, TestContext.Current.CancellationToken);

        // Assert
        hubContext.SentMethod.ShouldBe("UsageSnapshotUpdated");
        hubContext.SentArgs.ShouldHaveSingleItem();
        UsageSnapshotResponse dto = hubContext.SentArgs[0].ShouldBeOfType<UsageSnapshotResponse>();
        dto.AccountEmail.ShouldBe(snapshot.AccountEmail);
        dto.CapturedAt.ShouldBe(snapshot.CapturedAt);
    }

    [Fact]
    public async Task WhenSnapshotIsNull_SendsUsageSnapshotUpdatedWithNull()
    {
        // Arrange
        SpyHubContext hubContext = new();
        SignalRUsageSnapshotBroadcaster sut = new(hubContext);

        // Act
        await sut.SendSnapshotAsync(null, TestContext.Current.CancellationToken);

        // Assert
        hubContext.SentMethod.ShouldBe("UsageSnapshotUpdated");
        hubContext.SentArgs.ShouldHaveSingleItem();
        hubContext.SentArgs[0].ShouldBeNull();
    }

    private sealed class SpyHubContext : IHubContext<SystemNotificationHub>
    {
        private readonly SpyClientProxy _allProxy = new();

        public string? SentMethod => _allProxy.SentMethod;
        public object?[] SentArgs => _allProxy.SentArgs;

        IHubClients IHubContext<SystemNotificationHub>.Clients => new SpyHubClients(_allProxy);

        IGroupManager IHubContext<SystemNotificationHub>.Groups => null!;
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
