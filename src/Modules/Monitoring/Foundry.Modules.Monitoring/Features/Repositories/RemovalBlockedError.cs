using System.Globalization;

using Foundry.Shared;
using Foundry.Shared.Infrastructure.Http;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Foundry.Modules.Monitoring.Features.Repositories;

/// <summary>An issue whose live worker or Publisher refuses its repository's removal.</summary>
internal sealed record RemovalBlocker(string RepositorySlug, int IssueNumber, string State);

/// <summary>A removal refused because the listed issues still have work in flight; nothing was written.</summary>
internal sealed record RemovalBlockedError : Error
{
    public RemovalBlockedError(IReadOnlyList<RemovalBlocker> blockingIssues)
        : base(RepositoryErrors.RemovalBlockedCode, Describe(blockingIssues))
    {
        BlockingIssues = blockingIssues;
    }

    public IReadOnlyList<RemovalBlocker> BlockingIssues { get; }

    /// <summary>409 Conflict naming each blocking issue in <c>detail</c> and listing them in <c>blockingIssues</c>.</summary>
    public ProblemHttpResult ToConflictProblem() =>
        this.ToProblem(
            StatusCodes.Status409Conflict,
            new Dictionary<string, object?> { ["blockingIssues"] = BlockingIssues });

    private static string Describe(IReadOnlyList<RemovalBlocker> blockingIssues) =>
        "The repository cannot be removed while a worker is still active on: "
        + string.Join(
            ", ",
            blockingIssues.Select(b => string.Create(
                CultureInfo.InvariantCulture,
                $"{b.RepositorySlug}#{b.IssueNumber} ({b.State})")))
        + ". Try again once these finish.";
}
