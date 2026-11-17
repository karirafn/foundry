using System.Xml.Linq;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Queries.RepositoryDispatchQueriesTests;

public sealed class GetWorkerNuGetConfigAsync : IAsyncDisposable
{
    private const string SourceUrl = "https://gitlab.com/api/v4/groups/7/-/packages/nuget/index.json";
    private const string Token = "run-source-token";

    private const string Config = """
        <configuration>
          <packageSources>
            <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
            <add key="group" value="https://gitlab.com/api/v4/groups/7/-/packages/nuget/index.json" />
          </packageSources>
        </configuration>
        """;

    private readonly FoundryWebAppFactory _factory = new();

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task WhenTheRepositoryDeclaresAStoredSource_ReturnsTheConfigPointingAtTheProxyWithTheToken()
    {
        // Arrange
        (MonitoredRepositoryId repositoryId, PackageSourceId sourceId) = await SeedAsync(declares: true, storeSource: true);

        // Act
        WorkerNuGetConfig? result = await QueryAsync(repositoryId);

        // Assert
        XElement configuration = XDocument.Parse(result.ShouldNotBeNull().Xml).Root!;
        XElement add = configuration.Element("packageSources")!.Elements("add").Single(a => (string?)a.Attribute("key") == "group");
        ((string?)add.Attribute("value")).ShouldBe(
            $"http://host.docker.internal:5287/package-sources/{sourceId.Value}/api/v4/groups/7/-/packages/nuget/index.json");
        configuration.Element("packageSourceCredentials")!.Element("group")!.Elements("add")
            .Single(a => (string?)a.Attribute("key") == "ClearTextPassword")
            .Attribute("value")!.Value.ShouldBe(Token);
    }

    [Fact]
    public async Task WhenTheRepositoryDeclaresNoSource_ReturnsNothing()
    {
        // Arrange
        (MonitoredRepositoryId repositoryId, _) = await SeedAsync(declares: false, storeSource: true);

        // Act
        WorkerNuGetConfig? result = await QueryAsync(repositoryId);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public async Task WhenTheDeclaredSourceIsNotStoredYet_ReturnsNothing()
    {
        // Arrange
        (MonitoredRepositoryId repositoryId, _) = await SeedAsync(declares: true, storeSource: false);

        // Act
        WorkerNuGetConfig? result = await QueryAsync(repositoryId);

        // Assert
        result.ShouldBeNull();
    }

    private async Task<WorkerNuGetConfig?> QueryAsync(MonitoredRepositoryId repositoryId)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        IRepositoryDispatchQueries sut = scope.ServiceProvider.GetRequiredService<IRepositoryDispatchQueries>();
        return await sut.GetWorkerNuGetConfigAsync(repositoryId, Token, TestContext.Current.CancellationToken);
    }

    private async Task<(MonitoredRepositoryId RepositoryId, PackageSourceId SourceId)> SeedAsync(bool declares, bool storeSource)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        MonitoredRepository repository = MonitoredRepository.Create(
            RepositorySlug.Create("group/app").ValueOrThrow(),
            "gitlab.com",
            pollInterval: null).ValueOrThrow();
        repository.RecordRootListing(
            new RootNuGetConfig.Declared(
                "sha",
                Config,
                declares ? [DeclaredPackageSource.Create("group", SourceUrl).ValueOrThrow()] : []),
            listingETag: null);
        dbContext.Add(repository);

        PackageSource source = PackageSource.Create(new Uri(SourceUrl));
        if (storeSource)
        {
            dbContext.Add(source);
        }

        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (repository.Id, source.Id);
    }
}
