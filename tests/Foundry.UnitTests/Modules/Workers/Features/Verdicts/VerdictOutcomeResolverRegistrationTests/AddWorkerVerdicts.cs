using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Outcome;
using Foundry.Modules.Workers.Features.Verdicts;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Verdicts.VerdictOutcomeResolverRegistrationTests;

public sealed class AddWorkerVerdicts
{
    public static TheoryData<string> RoleNames() => [.. WorkerRole.All.Select(role => role.Name)];

    [Theory]
    [MemberData(nameof(RoleNames))]
    public void EveryRestrictedRoleHasExactlyOneVerdictOutcomeResolverAndNoFullRoleHasOne(string roleName)
    {
        // Arrange
        WorkerRole role = WorkerRole.FromStored(roleName);
        ServiceCollection services = new();
        services.AddSingleton<ILogger<ContainerOutputParser>>(NullLogger<ContainerOutputParser>.Instance);
        services.AddSingleton<IContainerOutputParser, ContainerOutputParser>();
        services.AddWorkerVerdicts();
        using ServiceProvider provider = services.BuildServiceProvider();

        // Act
        int registered = provider.GetServices<IVerdictOutcomeResolver>().Count(resolver => resolver.Role == role);

        // Assert
        registered.ShouldBe(role.Capability == WorkerCapability.Restricted ? 1 : 0);
    }
}
