using System.Reflection;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Testing;

using Microsoft.Extensions.Logging;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.UnreadableTokenMaterializationInterceptorTests;

public sealed class InitializedInstance
{
    [Fact]
    public void WhenCredentialTokenIsUnreadable_LogsWarningContainingCredentialIdAndAccountsToken()
    {
        // Arrange
        CapturingLogger logger = new();
        UnreadableTokenMaterializationInterceptor sut = new(new CapturingLoggerAdapter(logger));

        BaseUrl baseUrl = BaseUrl.Create("https://github.com").ValueOrThrow();
        GitHubCredential credential = GitHubCredential.Create("my-org", "ghp_abc", baseUrl);
        SetTokenUnreadable(credential);

        // Act
        sut.CheckAndWarn(credential);

        // Assert
        (LogLevel Level, string Message, Exception? Exception) entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Message.ShouldSatisfyAllConditions(
            () => entry.Message.ShouldContain(credential.Id.Value.ToString()),
            () => entry.Message.ShouldContain("accounts.token"));
    }

    [Fact]
    public void WhenCredentialTokenIsPresent_LogsNoWarning()
    {
        // Arrange
        CapturingLogger logger = new();
        UnreadableTokenMaterializationInterceptor sut = new(new CapturingLoggerAdapter(logger));

        BaseUrl baseUrl = BaseUrl.Create("https://github.com").ValueOrThrow();
        GitHubCredential credential = GitHubCredential.Create("my-org", "ghp_abc", baseUrl);

        // Act
        sut.CheckAndWarn(credential);

        // Assert
        logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public void WhenCredentialTokenIsNull_LogsNoWarning()
    {
        // Arrange
        CapturingLogger logger = new();
        UnreadableTokenMaterializationInterceptor sut = new(new CapturingLoggerAdapter(logger));

        BaseUrl baseUrl = BaseUrl.Create("https://github.com").ValueOrThrow();
        GitHubCredential credential = GitHubCredential.Create("my-org", null, baseUrl);

        // Act
        sut.CheckAndWarn(credential);

        // Assert
        logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public void WhenInstanceIsNotACredential_LogsNoWarning()
    {
        // Arrange
        CapturingLogger logger = new();
        UnreadableTokenMaterializationInterceptor sut = new(new CapturingLoggerAdapter(logger));

        // Act
        sut.CheckAndWarn("not a credential");

        // Assert
        logger.Entries.ShouldBeEmpty();
    }

    private static void SetTokenUnreadable(Credential credential)
    {
        // Token's setter is private protected — use reflection to reach it for test setup.
        // The Unreadable state is only reachable in production via a failed EF decryption;
        // reflection is the only test-safe way to reach it without an EF round-trip.
        PropertyInfo? property = typeof(Credential).GetProperty(
            nameof(Credential.Token),
            BindingFlags.Public | BindingFlags.Instance);

        property.ShouldNotBeNull();
        property.SetValue(credential, new ProviderToken.Unreadable());
    }

    /// <summary>
    /// Adapts a non-generic <see cref="CapturingLogger"/> to <see cref="ILogger{T}"/>
    /// so the interceptor can be constructed with a capturing logger.
    /// </summary>
    private sealed class CapturingLoggerAdapter(CapturingLogger inner)
        : ILogger<UnreadableTokenMaterializationInterceptor>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ((ILogger)inner).Log(logLevel, eventId, state, exception, formatter);
        }
    }
}
