using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.PackageSources;
using Foundry.Modules.Monitoring.Features.Providers.RootDirectory;
using Foundry.Shared;
using Foundry.Testing;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.PackageSources.RootNuGetConfigRefreshTests;

public sealed class RefreshAsync
{
    private const string ConfigSha = "44b4fc6d56897b048c772eb4087f854f46256132";
    private const string NewConfigSha = "45b983be36b73c0788dc9cbcb76cbb80fc7bb057";
    private const string FeedUrl = "https://gitlab.com/api/v4/groups/1/-/packages/nuget/index.json";

    private const string OneSourceConfig = $"""
        <configuration>
          <packageSources>
            <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
            <add key="gitlab" value="{FeedUrl}" />
          </packageSources>
        </configuration>
        """;

    private static MonitoredRepository NewRepository() =>
        MonitoredRepository.Create(RepositorySlug.Create("group/project").ValueOrThrow(), "gitlab.com", null)
            .ValueOrThrow();

    private static CountingIssueProvider ProviderListing(string? etag, params RootEntry[] entries) =>
        new() { RootListing = Result<RootDirectoryListing>.Ok(new RootDirectoryListing.Listed(entries, etag)) };

    private static RootEntry File(string name, string sha) => new(name, RootEntryKind.File, sha);

    private static Task RefreshRepositoryAsync(MonitoredRepository repository, CountingIssueProvider provider, ILogger? logger = null) =>
        RootNuGetConfigRefresh.RefreshAsync(repository, provider, logger ?? NullLogger.Instance, CancellationToken.None);

    private static void SeedDeclared(MonitoredRepository repository, string etag = "\"etag-1\"")
    {
        DeclaredPackageSource source = DeclaredPackageSource.Create("gitlab", FeedUrl).ValueOrThrow();
        repository.RecordRootListing(new RootNuGetConfig.Declared(ConfigSha, OneSourceConfig, [source]), etag);
    }

    [Theory]
    [InlineData("nuget.config")]
    [InlineData("NuGet.Config")]
    [InlineData("NuGet.config")]
    public async Task WhenRootHasANewConfig_ReadsItAndRecordsItsSources(string fileName)
    {
        // Arrange
        MonitoredRepository repository = NewRepository();
        CountingIssueProvider provider = ProviderListing("\"etag-1\"", File("README.md", "aaa"), File(fileName, ConfigSha));
        provider.Blobs[ConfigSha] = Result<string>.Ok(OneSourceConfig);

        // Act
        await RefreshRepositoryAsync(repository, provider);

        // Assert
        DeclaredPackageSource source = repository.DeclaredPackageSources.ShouldHaveSingleItem();
        repository.ShouldSatisfyAllConditions(
            () => source.Key.ShouldBe("gitlab"),
            () => source.ServiceIndexUrl.ShouldBe(new Uri(FeedUrl)),
            () => repository.RootNuGetConfigText.ShouldBe(OneSourceConfig),
            () => repository.RootNuGetConfigBlobSha.ShouldBe(ConfigSha),
            () => repository.RootListingETag.ShouldBe("\"etag-1\""));
    }

    [Fact]
    public async Task WhenTheConfigShaIsUnchanged_MakesNoFileRead()
    {
        // Arrange
        MonitoredRepository repository = NewRepository();
        SeedDeclared(repository);
        CountingIssueProvider provider = ProviderListing("\"etag-2\"", File("nuget.config", ConfigSha), File("new.txt", "bbb"));

        // Act
        await RefreshRepositoryAsync(repository, provider);

        // Assert
        repository.ShouldSatisfyAllConditions(
            () => provider.ReadBlobCallCount.ShouldBe(0),
            () => repository.DeclaredPackageSources.ShouldHaveSingleItem().Key.ShouldBe("gitlab"),
            () => repository.RootListingETag.ShouldBe("\"etag-2\""));
    }

    [Fact]
    public async Task WhenTheConfigShaChanged_ReplacesTheDeclaredSources()
    {
        // Arrange
        MonitoredRepository repository = NewRepository();
        SeedDeclared(repository);
        CountingIssueProvider provider = ProviderListing("\"etag-2\"", File("nuget.config", NewConfigSha));
        provider.Blobs[NewConfigSha] = Result<string>.Ok("""
            <configuration>
              <packageSources>
                <add key="other" value="https://pkgs.example.com/v3/index.json" />
              </packageSources>
            </configuration>
            """);

        // Act
        await RefreshRepositoryAsync(repository, provider);

        // Assert
        repository.DeclaredPackageSources.ShouldHaveSingleItem().Key.ShouldBe("other");
        repository.RootNuGetConfigBlobSha.ShouldBe(NewConfigSha);
    }

    [Fact]
    public async Task WhenItSendsTheListing_PassesTheStoredETag()
    {
        // Arrange
        MonitoredRepository repository = NewRepository();
        SeedDeclared(repository, etag: "W/\"stored\"");
        CountingIssueProvider provider = new()
        {
            RootListing = Result<RootDirectoryListing>.Ok(new RootDirectoryListing.Unchanged()),
        };

        // Act
        await RefreshRepositoryAsync(repository, provider);

        // Assert
        provider.LastIfNoneMatch.ShouldBe("W/\"stored\"");
    }

    [Fact]
    public async Task WhenTheRootIsUnchanged_KeepsTheStoredStateWithoutAFileRead()
    {
        // Arrange
        MonitoredRepository repository = NewRepository();
        SeedDeclared(repository);
        CountingIssueProvider provider = new()
        {
            RootListing = Result<RootDirectoryListing>.Ok(new RootDirectoryListing.Unchanged()),
        };

        // Act
        await RefreshRepositoryAsync(repository, provider);

        // Assert
        repository.ShouldSatisfyAllConditions(
            () => provider.ReadBlobCallCount.ShouldBe(0),
            () => repository.DeclaredPackageSources.ShouldHaveSingleItem(),
            () => repository.RootListingETag.ShouldBe("\"etag-1\""));
    }

    [Fact]
    public async Task WhenTheRootHasNoConfig_RecordsAnEmptyList()
    {
        // Arrange
        MonitoredRepository repository = NewRepository();
        SeedDeclared(repository);
        CountingIssueProvider provider = ProviderListing("\"etag-2\"", File("README.md", "aaa"));

        // Act
        await RefreshRepositoryAsync(repository, provider);

        // Assert
        repository.ShouldSatisfyAllConditions(
            () => repository.RootNuGetConfig.ShouldBeOfType<RootNuGetConfig.Absent>(),
            () => repository.DeclaredPackageSources.ShouldBeEmpty(),
            () => provider.ReadBlobCallCount.ShouldBe(0));
    }

    [Theory]
    [InlineData("Directory")]
    [InlineData("Symlink")]
    [InlineData("Submodule")]
    public async Task WhenTheConfigEntryIsNotAFile_TreatsItAsAbsent(string kindName)
    {
        // Arrange
        MonitoredRepository repository = NewRepository();
        CountingIssueProvider provider = ProviderListing(null, new RootEntry("nuget.config", Enum.Parse<RootEntryKind>(kindName), ConfigSha));

        // Act
        await RefreshRepositoryAsync(repository, provider);

        // Assert
        repository.RootNuGetConfig.ShouldBeOfType<RootNuGetConfig.Absent>();
        provider.ReadBlobCallCount.ShouldBe(0);
    }

    [Fact]
    public async Task WhenTwoConfigsDifferOnlyInCase_RecordsTheConfigAsInvalid()
    {
        // Arrange
        MonitoredRepository repository = NewRepository();
        SeedDeclared(repository);
        CountingIssueProvider provider = ProviderListing(
            "\"etag-2\"",
            File("nuget.config", ConfigSha),
            File("NuGet.Config", NewConfigSha));

        // Act
        await RefreshRepositoryAsync(repository, provider);

        // Assert
        repository.ShouldSatisfyAllConditions(
            () => repository.RootNuGetConfig.ShouldBeOfType<RootNuGetConfig.Invalid>().Message
                .ShouldBe("more than one root nuget.config"),
            () => repository.DeclaredPackageSources.ShouldBeEmpty(),
            () => provider.ReadBlobCallCount.ShouldBe(0));
    }

    [Fact]
    public async Task WhenTheConfigIsMalformed_RecordsItAsInvalid()
    {
        // Arrange
        MonitoredRepository repository = NewRepository();
        CountingIssueProvider provider = ProviderListing(null, File("nuget.config", ConfigSha));
        provider.Blobs[ConfigSha] = Result<string>.Ok("<configuration><packageSources></configuration>");

        // Act
        await RefreshRepositoryAsync(repository, provider);

        // Assert
        repository.RootNuGetConfig.ShouldBeOfType<RootNuGetConfig.Invalid>().BlobSha.ShouldBe(ConfigSha);
    }

    [Fact]
    public async Task WhenTheListingFails_KeepsTheStoredStateAndLogsAWarning()
    {
        // Arrange
        MonitoredRepository repository = NewRepository();
        SeedDeclared(repository);
        CountingIssueProvider provider = new()
        {
            RootListing = Result<RootDirectoryListing>.Fail(new Error("GitHub.UnexpectedStatusCode", "HTTP 500.")),
        };
        CapturingLogger logger = new();

        // Act
        await RefreshRepositoryAsync(repository, provider, logger);

        // Assert
        repository.ShouldSatisfyAllConditions(
            () => repository.DeclaredPackageSources.ShouldHaveSingleItem().Key.ShouldBe("gitlab"),
            () => repository.RootListingETag.ShouldBe("\"etag-1\""),
            () => logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Warning));
    }

    [Fact]
    public async Task WhenTheFileReadFails_KeepsTheStoredStateAndETagAndLogsAWarning()
    {
        // Arrange
        MonitoredRepository repository = NewRepository();
        SeedDeclared(repository);
        CountingIssueProvider provider = ProviderListing("\"etag-2\"", File("nuget.config", NewConfigSha));
        CapturingLogger logger = new();

        // Act
        await RefreshRepositoryAsync(repository, provider, logger);

        // Assert
        repository.ShouldSatisfyAllConditions(
            () => repository.DeclaredPackageSources.ShouldHaveSingleItem().Key.ShouldBe("gitlab"),
            () => repository.RootNuGetConfigBlobSha.ShouldBe(ConfigSha),
            () => repository.RootListingETag.ShouldBe("\"etag-1\""),
            () => logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Warning));
    }

    [Fact]
    public async Task WhenTheProviderDoesNotSupportTheListing_KeepsTheStoredStateWithoutAWarning()
    {
        // Arrange
        MonitoredRepository repository = NewRepository();
        SeedDeclared(repository);
        CountingIssueProvider provider = new()
        {
            RootListing = Result<RootDirectoryListing>.Fail(RootDirectoryErrors.NotSupported()),
        };
        CapturingLogger logger = new();

        // Act
        await RefreshRepositoryAsync(repository, provider, logger);

        // Assert
        repository.DeclaredPackageSources.ShouldHaveSingleItem();
        logger.Entries.ShouldNotContain(entry => entry.Level >= LogLevel.Warning);
    }
}
