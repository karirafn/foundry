namespace Foundry.Modules.Workers.Contracts;

public sealed record WorkerReportSummary(
    Guid Id,
    Guid WorkerRunId,
    int SequenceNumber,
    string ReportType,
    string Content,
    DateTimeOffset IngestedAt);
