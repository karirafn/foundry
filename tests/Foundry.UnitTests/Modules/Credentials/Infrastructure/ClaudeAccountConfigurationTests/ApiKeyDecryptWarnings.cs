using Foundry.Modules.Credentials.Domain.ValueObjects;
using Foundry.Modules.Credentials.Infrastructure.Configurations;
using Foundry.Testing;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Infrastructure.ClaudeAccountConfigurationTests;

public sealed class ApiKeyDecryptWarnings
{
    private const string StoredPurpose = "Foundry.Settings.Encryption";
    private const string ExpectedColumn = "claude_account.api_key";

    private readonly CapturingLogger<ApiKeyCredentialConverter> _logger = new();
    private readonly ApiKeyCredentialConverter _converter;

    public ApiKeyDecryptWarnings()
    {
        _converter = new ApiKeyCredentialConverter(DataProtectionProvider.Create("TestApp"), _logger);
    }

    [Fact]
    public void WhenCiphertextIsFromAnotherKeyRing_LogsWarningNamingTheColumn()
    {
        // Arrange
        IDataProtector foreign = DataProtectionProvider
            .Create("DifferentApp")
            .CreateProtector(StoredPurpose);
        string undecryptable = Convert.ToBase64String(foreign.Protect(System.Text.Encoding.UTF8.GetBytes("key")));

        // Act
        object? credential = _converter.ConvertFromProvider(undecryptable);

        // Assert
        credential.ShouldBeOfType<ApiKeyCredential.Unreadable>();
        AssertSingleWarningNamesColumn();
    }

    [Fact]
    public void WhenStoredValueIsNotBase64_LogsWarningNamingTheColumn()
    {
        // Arrange
        const string notBase64 = "not-valid-base64!!!";

        // Act
        object? credential = _converter.ConvertFromProvider(notBase64);

        // Assert
        credential.ShouldBeOfType<ApiKeyCredential.Unreadable>();
        AssertSingleWarningNamesColumn();
    }

    private void AssertSingleWarningNamesColumn()
    {
        (LogLevel Level, string Message, Exception? Exception) entry = _logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Message.ShouldContain(ExpectedColumn);
    }
}
