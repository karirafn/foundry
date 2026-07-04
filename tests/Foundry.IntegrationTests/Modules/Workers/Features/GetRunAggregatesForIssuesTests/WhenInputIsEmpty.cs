using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Contracts.Queries;

using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Workers.Features.GetRunAggregatesForIssuesTests;

public sealed class WhenInputIsEmpty : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;

    public WhenInputIsEmpty()
    {
        _factory = new FoundryWebAppFactory();
        _ = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task WhenEmptyIssueIds_ReturnsEmptyDictionary()
    {
        // Arrange
        using IServiceScope scope = _factory.Services.CreateScope();
        IWorkerRunQueries sut = scope.ServiceProvider.GetRequiredService<IWorkerRunQueries>();

        // Act
        IReadOnlyDictionary<Guid, RunAggregate> result = await sut.GetRunAggregatesForIssuesAsync(
            [],
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeEmpty();
    }
}
