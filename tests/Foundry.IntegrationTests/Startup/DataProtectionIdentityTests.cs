using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Startup;

/// <summary>
/// Pins the Data Protection identity of the running host: the fixed application name
/// that stored secrets are encrypted under, and the framework's content-root-derived
/// default discriminator that earlier installs encrypted under.
/// </summary>
public sealed class DataProtectionIdentityTests : IAsyncDisposable
{
    private const string ExpectedApplicationName = "Foundry";

    private readonly FoundryWebAppFactory _factory;

    // Creating the HttpClient triggers WebApplicationFactory to boot the host.
    private readonly HttpClient _client;

    public DataProtectionIdentityTests()
    {
        _factory = new FoundryWebAppFactory();
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public void WhenHostStarts_ApplicationDiscriminatorIsTheFixedApplicationName()
    {
        // Arrange
        IOptions<DataProtectionOptions> options = _factory.Services.GetRequiredService<IOptions<DataProtectionOptions>>();

        // Act
        string? discriminator = options.Value.ApplicationDiscriminator;

        // Assert
        discriminator.ShouldBe(ExpectedApplicationName);
    }

    [Fact]
    public void WhenHostStarts_DefaultUniqueIdentifierIsTheContentRootWithTrailingSeparator()
    {
        // Arrange
        IHostEnvironment environment = _factory.Services.GetRequiredService<IHostEnvironment>();
        string expected = Path.TrimEndingDirectorySeparator(environment.ContentRootPath) + Path.DirectorySeparatorChar;

        // Act
        string? identifier = _factory.Services.GetApplicationUniqueIdentifier();

        // Assert
        identifier.ShouldBe(expected);
    }
}
