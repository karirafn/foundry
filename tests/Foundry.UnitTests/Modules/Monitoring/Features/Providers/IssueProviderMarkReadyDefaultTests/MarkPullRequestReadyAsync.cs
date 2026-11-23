using Foundry.Modules.Monitoring.Contracts.Commands;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Providers.IssueProviderMarkReadyDefaultTests;

/// <summary>The default mark-ready implementation for providers that have none.</summary>
public sealed class MarkPullRequestReadyAsync
{
    [Fact]
    public async Task Default_FailsPermanentWithoutAnyProviderCall()
    {
        // Arrange
        CountingIssueProvider provider = new();
        IIssueProvider sut = provider;

        // Act
        Result result = await sut.MarkPullRequestReadyAsync(
            RepositorySlug.Create("owner/repo").ValueOrThrow(),
            "https://github.com/owner/repo/pull/42",
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.ErrorOrThrow().Code.ShouldBe(RunPublishingErrors.PermanentCode),
            () => provider.TotalCalls.ShouldBe(0));
    }
}
