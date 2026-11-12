using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Providers.BlockerLinks;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Providers.BlockerLinks;

public sealed class BlockerLinkErrorsTests
{
    private const int IssueNumber = 42;
    private const int BlockerNumber = 15;

    [Fact]
    public async Task ProviderWithoutBlockerLinks_FailsPermanentlyWithoutAnyProviderCall()
    {
        // Arrange
        CountingIssueProvider counting = new();
        IIssueProvider provider = counting;
        RepositorySlug slug = RepositorySlug.Create("owner/repo").ValueOrThrow();

        // Act
        Result result = await provider.AddBlockerAsync(slug, IssueNumber, BlockerNumber, CancellationToken.None);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.ShouldBeOfType<Result.Failure>().Error.Code.ShouldBe(BlockerLinkErrors.PermanentCode),
            () => counting.TotalCalls.ShouldBe(0));
    }

    [Fact]
    public void WhenErrorIsTransient_IsTransientIsTrueAndPermanentIsFalse()
    {
        // Arrange
        Error transient = BlockerLinkErrors.Transient("rate limited");
        Error permanent = BlockerLinkErrors.Permanent("not found");

        // Act
        bool transientResult = BlockerLinkErrors.IsTransient(transient);
        bool permanentResult = BlockerLinkErrors.IsTransient(permanent);

        // Assert
        (transientResult, permanentResult).ShouldBe((true, false));
    }
}
