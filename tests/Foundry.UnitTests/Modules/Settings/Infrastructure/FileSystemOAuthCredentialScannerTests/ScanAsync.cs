using Foundry.Modules.Settings.Domain;
using Foundry.Modules.Settings.Infrastructure;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Infrastructure.FileSystemOAuthCredentialScannerTests;

public sealed class ScanAsync
{
    private const string PrimaryPath = "/home/user/.claude/.credentials.json";
    private const string SecondaryPath = "/appdata/claude/.credentials.json";

    private readonly FakeFileSystem _fileSystem;

    public ScanAsync()
    {
        _fileSystem = new FakeFileSystem();
    }

    private FileSystemOAuthCredentialScanner CreateSut(params string[] paths) =>
        new(_fileSystem, paths.Length > 0 ? paths : [PrimaryPath]);

    [Fact]
    public async Task WhenValidCredentialsFileExists_ReturnsCredentials()
    {
        // Arrange
        DateTimeOffset expiresAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        string json = $$"""
            {
              "claudeAiOauth": {
                "accessToken": "my-access-token",
                "refreshToken": "my-refresh-token",
                "expiresAt": "{{expiresAt:O}}",
                "subscriptionType": "max"
              }
            }
            """;
        _fileSystem.AddFile(PrimaryPath, json);
        FileSystemOAuthCredentialScanner sut = CreateSut(PrimaryPath);

        // Act
        Result<OAuthCredentials> result = await sut.ScanAsync(TestContext.Current.CancellationToken);

        // Assert
        Result<OAuthCredentials>.Success success = result.ShouldBeOfType<Result<OAuthCredentials>.Success>();
        success.Value.ShouldSatisfyAllConditions(
            () => success.Value.AccessToken.ShouldBe("my-access-token"),
            () => success.Value.RefreshToken.ShouldBe("my-refresh-token"),
            () => success.Value.ExpiresAt.ShouldBe(expiresAt),
            () => success.Value.SubscriptionType.ShouldBe("max"));
    }

    [Fact]
    public async Task WhenNoFileExists_ReturnsOAuthCredentialsNotFoundError()
    {
        // Arrange
        FileSystemOAuthCredentialScanner sut = CreateSut(PrimaryPath);

        // Act
        Result<OAuthCredentials> result = await sut.ScanAsync(TestContext.Current.CancellationToken);

        // Assert
        Result<OAuthCredentials>.Failure failure = result.ShouldBeOfType<Result<OAuthCredentials>.Failure>();
        failure.Error.Code.ShouldBe(SettingsErrors.OAuthCredentialsNotFoundCode);
    }

    [Fact]
    public async Task WhenFileExistsButMissingAccessToken_ReturnsError()
    {
        // Arrange
        DateTimeOffset expiresAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        string json = $$"""
            {
              "claudeAiOauth": {
                "refreshToken": "my-refresh-token",
                "expiresAt": "{{expiresAt:O}}",
                "subscriptionType": "max"
              }
            }
            """;
        _fileSystem.AddFile(PrimaryPath, json);
        FileSystemOAuthCredentialScanner sut = CreateSut(PrimaryPath);

        // Act
        Result<OAuthCredentials> result = await sut.ScanAsync(TestContext.Current.CancellationToken);

        // Assert
        Result<OAuthCredentials>.Failure failure = result.ShouldBeOfType<Result<OAuthCredentials>.Failure>();
        failure.Error.Code.ShouldBe(SettingsErrors.OAuthCredentialsNotFoundCode);
    }

    [Fact]
    public async Task WhenFileExistsButMissingRefreshToken_ReturnsError()
    {
        // Arrange
        DateTimeOffset expiresAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        string json = $$"""
            {
              "claudeAiOauth": {
                "accessToken": "my-access-token",
                "expiresAt": "{{expiresAt:O}}",
                "subscriptionType": "max"
              }
            }
            """;
        _fileSystem.AddFile(PrimaryPath, json);
        FileSystemOAuthCredentialScanner sut = CreateSut(PrimaryPath);

        // Act
        Result<OAuthCredentials> result = await sut.ScanAsync(TestContext.Current.CancellationToken);

        // Assert
        Result<OAuthCredentials>.Failure failure = result.ShouldBeOfType<Result<OAuthCredentials>.Failure>();
        failure.Error.Code.ShouldBe(SettingsErrors.OAuthCredentialsNotFoundCode);
    }

    [Fact]
    public async Task WhenFileExistsButMissingOAuthSection_ReturnsError()
    {
        // Arrange
        string json = """{ "otherData": "value" }""";
        _fileSystem.AddFile(PrimaryPath, json);
        FileSystemOAuthCredentialScanner sut = CreateSut(PrimaryPath);

        // Act
        Result<OAuthCredentials> result = await sut.ScanAsync(TestContext.Current.CancellationToken);

        // Assert
        Result<OAuthCredentials>.Failure failure = result.ShouldBeOfType<Result<OAuthCredentials>.Failure>();
        failure.Error.Code.ShouldBe(SettingsErrors.OAuthCredentialsNotFoundCode);
    }

    [Fact]
    public async Task WhenFirstPathInvalidSecondPathValid_ReturnsCredentialsFromSecondPath()
    {
        // Arrange
        DateTimeOffset expiresAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        string validJson = $$"""
            {
              "claudeAiOauth": {
                "accessToken": "secondary-token",
                "refreshToken": "secondary-refresh",
                "expiresAt": "{{expiresAt:O}}",
                "subscriptionType": "pro"
              }
            }
            """;
        _fileSystem.AddFile(SecondaryPath, validJson);
        FileSystemOAuthCredentialScanner sut = CreateSut(PrimaryPath, SecondaryPath);

        // Act
        Result<OAuthCredentials> result = await sut.ScanAsync(TestContext.Current.CancellationToken);

        // Assert
        Result<OAuthCredentials>.Success success = result.ShouldBeOfType<Result<OAuthCredentials>.Success>();
        success.Value.AccessToken.ShouldBe("secondary-token");
    }

    [Fact]
    public async Task WhenFirstPathValidSecondPathAlsoValid_ReturnsCredentialsFromFirstPath()
    {
        // Arrange
        DateTimeOffset expiresAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        string primaryJson = $$"""
            {
              "claudeAiOauth": {
                "accessToken": "primary-token",
                "refreshToken": "primary-refresh",
                "expiresAt": "{{expiresAt:O}}",
                "subscriptionType": "max"
              }
            }
            """;
        string secondaryJson = $$"""
            {
              "claudeAiOauth": {
                "accessToken": "secondary-token",
                "refreshToken": "secondary-refresh",
                "expiresAt": "{{expiresAt:O}}",
                "subscriptionType": "pro"
              }
            }
            """;
        _fileSystem.AddFile(PrimaryPath, primaryJson);
        _fileSystem.AddFile(SecondaryPath, secondaryJson);
        FileSystemOAuthCredentialScanner sut = CreateSut(PrimaryPath, SecondaryPath);

        // Act
        Result<OAuthCredentials> result = await sut.ScanAsync(TestContext.Current.CancellationToken);

        // Assert
        Result<OAuthCredentials>.Success success = result.ShouldBeOfType<Result<OAuthCredentials>.Success>();
        success.Value.AccessToken.ShouldBe("primary-token");
    }

    [Fact]
    public async Task WhenExpiresAtIsUnixTimestamp_ReturnsCredentials()
    {
        // Arrange
        long unixMs = 1735689600000;
        DateTimeOffset expectedExpiry = DateTimeOffset.FromUnixTimeMilliseconds(unixMs);
        string json = $$"""
            {
              "claudeAiOauth": {
                "accessToken": "my-access-token",
                "refreshToken": "my-refresh-token",
                "expiresAt": {{unixMs}},
                "subscriptionType": "max"
              }
            }
            """;
        _fileSystem.AddFile(PrimaryPath, json);
        FileSystemOAuthCredentialScanner sut = CreateSut(PrimaryPath);

        // Act
        Result<OAuthCredentials> result = await sut.ScanAsync(TestContext.Current.CancellationToken);

        // Assert
        Result<OAuthCredentials>.Success success = result.ShouldBeOfType<Result<OAuthCredentials>.Success>();
        success.Value.ExpiresAt.ShouldBe(expectedExpiry);
    }

    [Fact]
    public async Task WhenFileReadThrowsIoException_ReturnsError()
    {
        // Arrange
        _fileSystem.AddFile(PrimaryPath, content: null);
        FileSystemOAuthCredentialScanner sut = CreateSut(PrimaryPath);

        // Act
        Result<OAuthCredentials> result = await sut.ScanAsync(TestContext.Current.CancellationToken);

        // Assert
        Result<OAuthCredentials>.Failure failure = result.ShouldBeOfType<Result<OAuthCredentials>.Failure>();
        failure.Error.Code.ShouldBe(SettingsErrors.OAuthCredentialsNotFoundCode);
    }
}

internal sealed class FakeFileSystem : IFileSystem
{
    private readonly Dictionary<string, string?> _files = [];

    public void AddFile(string path, string? content) => _files[path] = content;

    public bool FileExists(string path) => _files.ContainsKey(path);

    public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken)
    {
        if (_files[path] is null)
        {
            throw new IOException($"Simulated read failure for path: {path}");
        }

        return Task.FromResult(_files[path]!);
    }
}
