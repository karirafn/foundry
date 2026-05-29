namespace Foundry.Modules.Workers.Features;

internal sealed record WorkerReportPayload(
    string Type,
    string Status,
    string? Summary,
    string? Error,
    string? PrUrl,
    string? BranchName,
    WorkerReportMetrics? Metrics);

internal sealed record WorkerReportMetrics(
    int TestsRun,
    int TestsPassed);
