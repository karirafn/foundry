using Foundry.WebApi.Modules.Workers.Domain;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Modules.Workers.Domain.WorkerReportTests;

public sealed class Create
{
    [Fact]
    public void WhenCalled_AssignsNewId()
    {
        // Arrange
        WorkerRunId runId = WorkerRunId.New();

        // Act
        WorkerReport report = WorkerReport.Create(runId, 1, "progress", "{}");

        // Assert
        report.Id.Value.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public void WhenCalled_SetsWorkerRunId()
    {
        // Arrange
        WorkerRunId runId = WorkerRunId.New();

        // Act
        WorkerReport report = WorkerReport.Create(runId, 1, "progress", "{}");

        // Assert
        report.WorkerRunId.ShouldBe(runId);
    }

    [Fact]
    public void WhenCalled_SetsSequenceNumber()
    {
        // Arrange
        WorkerRunId runId = WorkerRunId.New();

        // Act
        WorkerReport report = WorkerReport.Create(runId, 42, "progress", "{}");

        // Assert
        report.SequenceNumber.ShouldBe(42);
    }

    [Fact]
    public void WhenCalled_SetsReportType()
    {
        // Arrange
        WorkerRunId runId = WorkerRunId.New();

        // Act
        WorkerReport report = WorkerReport.Create(runId, 1, "final", "{\"exitCode\":0}");

        // Assert
        report.ReportType.ShouldBe("final");
    }

    [Fact]
    public void WhenCalled_SetsContent()
    {
        // Arrange
        WorkerRunId runId = WorkerRunId.New();
        string content = """{"step":"build","status":"ok"}""";

        // Act
        WorkerReport report = WorkerReport.Create(runId, 1, "progress", content);

        // Assert
        report.Content.ShouldBe(content);
    }

    [Fact]
    public void WhenCalled_SetsIngestedAtToUtcNow()
    {
        // Arrange
        WorkerRunId runId = WorkerRunId.New();
        DateTimeOffset before = DateTimeOffset.UtcNow;

        // Act
        WorkerReport report = WorkerReport.Create(runId, 1, "progress", "{}");

        // Assert
        DateTimeOffset after = DateTimeOffset.UtcNow;
        report.IngestedAt.ShouldBeInRange(before, after);
    }
}
