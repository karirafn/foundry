using System.Text.Json;

using Foundry.Modules.Credentials.Domain.ValueObjects;
using Foundry.Modules.Credentials.Infrastructure;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Infrastructure.CredentialValidityJsonConverterTests;

public sealed class RoundTrip
{
    private readonly JsonSerializerOptions _options;

    public RoundTrip()
    {
        _options = new JsonSerializerOptions();
        _options.Converters.Add(new CredentialValidityJsonConverter());
    }

    [Fact]
    public void WhenValidRoundTripped_PreservesType()
    {
        // Arrange
        CredentialValidity original = new CredentialValidity.Valid();

        // Act
        string json = JsonSerializer.Serialize(original, _options);
        CredentialValidity? result = JsonSerializer.Deserialize<CredentialValidity>(json, _options);

        // Assert
        result.ShouldBeOfType<CredentialValidity.Valid>();
    }

    [Fact]
    public void WhenInvalidRoundTripped_PreservesReason()
    {
        // Arrange
        CredentialValidity original = new CredentialValidity.Invalid("worker_auth_failed");

        // Act
        string json = JsonSerializer.Serialize(original, _options);
        CredentialValidity? result = JsonSerializer.Deserialize<CredentialValidity>(json, _options);

        // Assert
        CredentialValidity.Invalid invalid = result.ShouldBeOfType<CredentialValidity.Invalid>();
        invalid.Reason.ShouldBe("worker_auth_failed");
    }

    [Fact]
    public void WhenUnknownTypeDiscriminator_ThrowsJsonException()
    {
        // Arrange
        string json = @"{""type"":""unknown""}";

        // Act
        Action act = () => JsonSerializer.Deserialize<CredentialValidity>(json, _options);

        // Assert
        Should.Throw<JsonException>(act);
    }
}
