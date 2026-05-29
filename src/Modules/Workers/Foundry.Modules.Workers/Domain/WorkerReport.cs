using Foundry.Shared;

namespace Foundry.Modules.Workers.Domain;

public sealed class WorkerReport : Entity<WorkerReportId>
{
    // Private parameterless constructor for EF Core materialization.
    private WorkerReport()
        : base(WorkerReportId.New())
    {
    }

    private WorkerReport(
        WorkerReportId id,
        WorkerRunId workerRunId,
        int sequenceNumber,
        string reportType,
        string content,
        DateTimeOffset ingestedAt)
        : base(id)
    {
        WorkerRunId = workerRunId;
        SequenceNumber = sequenceNumber;
        ReportType = reportType;
        Content = content;
        IngestedAt = ingestedAt;
    }

    public WorkerRunId WorkerRunId { get; private set; }

    public int SequenceNumber { get; private set; }

    public string ReportType { get; private set; } = string.Empty;

    public string Content { get; private set; } = string.Empty;

    public DateTimeOffset IngestedAt { get; private set; }

    public static WorkerReport Create(
        WorkerRunId workerRunId,
        int sequenceNumber,
        string reportType,
        string content)
    {
        return new WorkerReport(
            WorkerReportId.New(),
            workerRunId,
            sequenceNumber,
            reportType,
            content,
            DateTimeOffset.UtcNow);
    }
}
