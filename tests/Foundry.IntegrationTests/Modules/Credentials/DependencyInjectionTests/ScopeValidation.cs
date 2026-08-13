using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Credentials.DependencyInjectionTests;

/// <summary>
/// Validates that the Credentials module DI registrations form a coherent graph with no
/// singleton-captures-scoped violations. This test failed when
/// <c>CreditProbeCoordinator</c> (singleton) captured <c>IIntegrationEventProcessor</c>
/// (scoped) via constructor injection instead of resolving it per-invocation from a scope.
/// </summary>
public sealed class ScopeValidation
{
    [Fact]
    public void WhenServicesAreBuilt_ScopeValidationPasses()
    {
        // Arrange
        IServiceCollection? capturedServices = null;

        FoundryWebAppFactory factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            capturedServices = services;
        });

        // Access factory.Services to trigger host build and ConfigureServices invocation.
        // The override runs before BuildServiceProvider — we get the full collection.
        _ = factory.Services;

        capturedServices.ShouldNotBeNull();

        // Act — build a fresh provider with scope validation enabled.
        // A singleton-captures-scoped registration throws InvalidOperationException here.
        Action buildWithValidation = () => capturedServices.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        // Assert
        buildWithValidation.ShouldNotThrow();

        factory.Dispose();
    }
}
