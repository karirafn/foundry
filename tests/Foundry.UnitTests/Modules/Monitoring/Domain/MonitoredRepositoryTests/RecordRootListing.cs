using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.MonitoredRepositoryTests;

public sealed class RecordRootListing
{
    private const string BlobSha = "44b4fc6d56897b048c772eb4087f854f46256132";
    private const string RawText = "<configuration />";

    private static RepositorySlug ValidSlug => RepositorySlug.Create("octocat/hello-world").ValueOrThrow();

    private static DeclaredPackageSource Source(string key, string url) =>
        DeclaredPackageSource.Create(key, url).ValueOrThrow();

    private static MonitoredRepository NewRepository() =>
        MonitoredRepository.Create(ValidSlug, "github.com", null).ValueOrThrow();

    [Fact]
    public void WhenNeverRecorded_DeclaresNoSources()
    {
        // Act
        MonitoredRepository repository = NewRepository();

        // Assert
        repository.ShouldSatisfyAllConditions(
            () => repository.RootNuGetConfig.ShouldBeOfType<RootNuGetConfig.Absent>(),
            () => repository.DeclaredPackageSources.ShouldBeEmpty(),
            () => repository.RootListingETag.ShouldBeNull());
    }

    [Fact]
    public void WhenADeclaredConfigIsRecorded_StoresItsSourcesTextShaAndTheETag()
    {
        // Arrange
        MonitoredRepository repository = NewRepository();
        DeclaredPackageSource source = Source("gitlab", "https://gitlab.com/api/v4/groups/1/-/packages/nuget/index.json");

        // Act
        repository.RecordRootListing(new RootNuGetConfig.Declared(BlobSha, RawText, [source]), "\"etag-1\"");

        // Assert
        repository.ShouldSatisfyAllConditions(
            () => repository.DeclaredPackageSources.ShouldBe([source]),
            () => repository.RootNuGetConfigText.ShouldBe(RawText),
            () => repository.RootNuGetConfigBlobSha.ShouldBe(BlobSha),
            () => repository.RootNuGetConfigInvalidReason.ShouldBeNull(),
            () => repository.RootListingETag.ShouldBe("\"etag-1\""));
    }

    [Fact]
    public void WhenADeclaredConfigIsRecorded_ReadsBackAsThatConfig()
    {
        // Arrange
        MonitoredRepository repository = NewRepository();
        DeclaredPackageSource source = Source("feed", "https://pkgs.example.com/v3/index.json");

        // Act
        repository.RecordRootListing(new RootNuGetConfig.Declared(BlobSha, RawText, [source]), listingETag: null);

        // Assert
        RootNuGetConfig.Declared declared = repository.RootNuGetConfig.ShouldBeOfType<RootNuGetConfig.Declared>();
        declared.BlobSha.ShouldBe(BlobSha);
        declared.RawText.ShouldBe(RawText);
        declared.Sources.ShouldBe([source]);
    }

    [Fact]
    public void WhenAnInvalidConfigReplacesADeclaredOne_ClearsTheSourcesAndKeepsTheMessage()
    {
        // Arrange
        MonitoredRepository repository = NewRepository();
        repository.RecordRootListing(
            new RootNuGetConfig.Declared(BlobSha, RawText, [Source("feed", "https://pkgs.example.com/v3/index.json")]),
            "\"etag-1\"");

        // Act
        repository.RecordRootListing(new RootNuGetConfig.Invalid("abc123", "<broken", "Unexpected end of file."), "\"etag-2\"");

        // Assert
        RootNuGetConfig.Invalid invalid = repository.RootNuGetConfig.ShouldBeOfType<RootNuGetConfig.Invalid>();
        repository.ShouldSatisfyAllConditions(
            () => invalid.Message.ShouldBe("Unexpected end of file."),
            () => invalid.BlobSha.ShouldBe("abc123"),
            () => invalid.RawText.ShouldBe("<broken"),
            () => repository.DeclaredPackageSources.ShouldBeEmpty(),
            () => repository.RootListingETag.ShouldBe("\"etag-2\""));
    }

    [Fact]
    public void WhenTheConfigIsRemoved_ClearsEverythingButTheETag()
    {
        // Arrange
        MonitoredRepository repository = NewRepository();
        repository.RecordRootListing(
            new RootNuGetConfig.Declared(BlobSha, RawText, [Source("feed", "https://pkgs.example.com/v3/index.json")]),
            "\"etag-1\"");

        // Act
        repository.RecordRootListing(new RootNuGetConfig.Absent(), "\"etag-2\"");

        // Assert
        repository.ShouldSatisfyAllConditions(
            () => repository.RootNuGetConfig.ShouldBeOfType<RootNuGetConfig.Absent>(),
            () => repository.DeclaredPackageSources.ShouldBeEmpty(),
            () => repository.RootNuGetConfigText.ShouldBeNull(),
            () => repository.RootNuGetConfigBlobSha.ShouldBeNull(),
            () => repository.RootNuGetConfigInvalidReason.ShouldBeNull(),
            () => repository.RootListingETag.ShouldBe("\"etag-2\""));
    }

    [Fact]
    public void WhenMoreThanOneConfigIsRecorded_ReadsBackAsInvalidWithoutAFile()
    {
        // Arrange
        MonitoredRepository repository = NewRepository();

        // Act
        repository.RecordRootListing(RootNuGetConfig.MoreThanOneConfig(), "\"etag-1\"");

        // Assert
        RootNuGetConfig.Invalid invalid = repository.RootNuGetConfig.ShouldBeOfType<RootNuGetConfig.Invalid>();
        invalid.Message.ShouldBe("more than one root nuget.config");
        invalid.BlobSha.ShouldBeNull();
    }
}
