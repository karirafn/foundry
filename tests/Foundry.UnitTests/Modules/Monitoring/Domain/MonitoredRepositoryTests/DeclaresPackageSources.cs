using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.MonitoredRepositoryTests;

public sealed class DeclaresPackageSources
{
    private const string BlobSha = "44b4fc6d56897b048c772eb4087f854f46256132";
    private const string RawText = "<configuration />";

    private static MonitoredRepository NewRepository() =>
        MonitoredRepository.Create(RepositorySlug.Create("octocat/hello-world").ValueOrThrow(), "github.com", null).ValueOrThrow();

    [Fact]
    public void WhenTheRootConfigDeclaresASource_IsTrue()
    {
        // Arrange
        MonitoredRepository repository = NewRepository();
        DeclaredPackageSource source = DeclaredPackageSource
            .Create("gitlab", "https://gitlab.com/api/v4/groups/1/-/packages/nuget/index.json")
            .ValueOrThrow();

        // Act
        repository.RecordRootListing(new RootNuGetConfig.Declared(BlobSha, RawText, [source]), listingETag: null);

        // Assert
        repository.DeclaresPackageSources.ShouldBeTrue();
    }

    [Fact]
    public void WhenTheRootConfigDeclaresNoSource_IsFalse()
    {
        // Arrange
        MonitoredRepository repository = NewRepository();

        // Act
        repository.RecordRootListing(new RootNuGetConfig.Declared(BlobSha, RawText, []), listingETag: null);

        // Assert
        repository.DeclaresPackageSources.ShouldBeFalse();
    }

    [Fact]
    public void WhenThereIsNoRootConfig_IsFalse()
    {
        NewRepository().DeclaresPackageSources.ShouldBeFalse();
    }
}
