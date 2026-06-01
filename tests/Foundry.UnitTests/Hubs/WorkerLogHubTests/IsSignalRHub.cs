using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;

using Foundry.WebApi.Hubs;

using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Hubs.WorkerLogHubTests;

public sealed class IsSignalRHub
{
    [Fact]
    public async Task WhenJoinIssueLogCalled_AddsConnectionToIssueGroup()
    {
        // Arrange
        Guid issueId = Guid.NewGuid();
        string expectedGroupName = $"issue-{issueId}";
        string connectionId = "conn-123";

        SpyGroupManager groups = new();
        StubHubCallerContext context = new(connectionId);

        WorkerLogHub sut = new();
        sut.Groups = groups;
        sut.Context = context;

        // Act
        await sut.JoinIssueLog(issueId);

        // Assert
        groups.AddedConnectionId.ShouldBe(connectionId);
        groups.AddedGroupName.ShouldBe(expectedGroupName);
    }

    private sealed class SpyGroupManager : IGroupManager
    {
        public string? AddedConnectionId { get; private set; }
        public string? AddedGroupName { get; private set; }

        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            AddedConnectionId = connectionId;
            AddedGroupName = groupName;
            return Task.CompletedTask;
        }

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class StubHubCallerContext(string connectionId) : HubCallerContext
    {
        public override string ConnectionId => connectionId;
        public override string? UserIdentifier => null;
        public override ClaimsPrincipal? User => null;
        public override IDictionary<object, object?> Items => new Dictionary<object, object?>();
        public override IFeatureCollection Features => new FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() { }
    }
}
