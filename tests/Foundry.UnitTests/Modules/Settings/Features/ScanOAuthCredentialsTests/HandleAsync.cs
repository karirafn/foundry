using Foundry.Modules.Settings.Domain;
using Foundry.Modules.Settings.Features;
using Foundry.Modules.Settings.Infrastructure;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Features.ScanOAuthCredentialsTests;

public sealed class HandleAsync
{
    [Fact]
    public async Task WhenScannerReturnsCredentials_ReturnsSuccessWithCredentials()
    {
        // Arrange
        DateTimeOffset expiresAt = new(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);
        OAuthCredentials credentials = new("access-token", "refresh-token", expiresAt, "pro");
        FakeOAuthCredentialScanner scanner = new(Result<OAuthCredentials>.Ok(credentials));
        ScanOAuthCredentials.Handler sut = new(scanner);

        // Act
        Result<OAuthCredentials> result = await sut.HandleAsync(
            new ScanOAuthCredentials.Query(),
            TestContext.Current.CancellationToken);

        // Assert
        Result<OAuthCredentials>.Success success = result.ShouldBeOfType<Result<OAuthCredentials>.Success>();
        success.Value.ShouldSatisfyAllConditions(
            () => success.Value.AccessToken.ShouldBe("access-token"),
            () => success.Value.RefreshToken.ShouldBe("refresh-token"),
            () => success.Value.ExpiresAt.ShouldBe(expiresAt),
            () => success.Value.SubscriptionType.ShouldBe("pro"));
    }

    [Fact]
    public async Task WhenScannerReturnsError_ReturnsFailureWithOAuthCredentialsNotFoundError()
    {
        // Arrange
        FakeOAuthCredentialScanner scanner = new(
            Result<OAuthCredentials>.Fail(SettingsErrors.OAuthCredentialsNotFound));
        ScanOAuthCredentials.Handler sut = new(scanner);

        // Act
        Result<OAuthCredentials> result = await sut.HandleAsync(
            new ScanOAuthCredentials.Query(),
            TestContext.Current.CancellationToken);

        // Assert
        Result<OAuthCredentials>.Failure failure = result.ShouldBeOfType<Result<OAuthCredentials>.Failure>();
        failure.Error.Code.ShouldBe(SettingsErrors.OAuthCredentialsNotFoundCode);
    }
}

internal sealed class FakeOAuthCredentialScanner(Result<OAuthCredentials> result) : IOAuthCredentialScanner
{
    public Task<Result<OAuthCredentials>> ScanAsync(CancellationToken cancellationToken) =>
        Task.FromResult(result);
}
