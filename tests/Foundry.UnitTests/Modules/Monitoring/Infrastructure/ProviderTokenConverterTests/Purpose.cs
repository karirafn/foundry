using Foundry.Modules.Monitoring.Infrastructure.Configurations;
using Foundry.Shared.Infrastructure.DataProtection;

using Microsoft.AspNetCore.DataProtection;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.ProviderTokenConverterTests;

public sealed class Purpose
{
    [Fact]
    public void ExposesItsProtectorPurpose()
    {
        // Arrange
        IDataProtectedConverter converter = new ProviderTokenConverter(DataProtectionProvider.Create("Foundry.Test"));

        // Act
        string purpose = converter.ProtectorPurpose;

        // Assert
        purpose.ShouldBe("Foundry.Monitoring.Encryption");
    }
}
