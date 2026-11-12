using System.Text.Json;

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
    public void WhenAnHttpRequestExceptionIsThrown_IsTransportFailure()
    {
        // Arrange
        HttpRequestException exception = new("connection refused");

        // Act
        bool result = BlockerLinkErrors.IsTransportFailure(exception, CancellationToken.None);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void WhenARequestTimesOutWithoutTheCallerCancelling_IsTransportFailure()
    {
        // Arrange
        TaskCanceledException exception = new();

        // Act
        bool result = BlockerLinkErrors.IsTransportFailure(exception, CancellationToken.None);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void WhenTheCallerCancelled_IsNotTransportFailure()
    {
        // Arrange
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        TaskCanceledException exception = new();

        // Act
        bool result = BlockerLinkErrors.IsTransportFailure(exception, cancellation.Token);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void WhenAParseExceptionIsThrown_IsNotTransportFailure()
    {
        // Arrange
        JsonException exception = new("bad json");

        // Act
        bool result = BlockerLinkErrors.IsTransportFailure(exception, CancellationToken.None);

        // Assert
        result.ShouldBeFalse();
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
