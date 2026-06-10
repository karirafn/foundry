namespace Foundry.Modules.Workers.Contracts;

public sealed record GetReportsResponse(
    IReadOnlyList<WorkerReportSummary> Reports,
    string? ContainerOutput);
