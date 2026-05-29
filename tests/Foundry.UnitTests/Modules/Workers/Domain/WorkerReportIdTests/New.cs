using Foundry.Modules.Workers.Domain;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.WorkerReportIdTests;

public sealed class New
{
    [Fact]
    public void WhenCalledTwice_ProducesDistinctIds()
    {
        // Arrange

        // Act
        WorkerReportId a = WorkerReportId.New();
        WorkerReportId b = WorkerReportId.New();

        // Assert
        a.ShouldNotBe(b);
    }
}
