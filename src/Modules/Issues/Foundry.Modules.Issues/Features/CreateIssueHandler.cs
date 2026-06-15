using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Issues.Features;

internal sealed class CreateIssueHandler(
    DbContext db,
    ILogger<CreateIssueHandler> logger) : IIntegrationEventHandler<IssueDetected>
{
    public async Task HandleAsync(IssueDetected @event, CancellationToken cancellationToken)
    {
        Result<IssueAuthor> author = IssueAuthor.Create(@event.Author);
        if (author is Result<IssueAuthor>.Failure authorFailure)
        {
            logger.LogWarning(
                "Skipping issue #{IssueNumber}: invalid author — {Error}",
                @event.IssueNumber,
                authorFailure.Error);
            return;
        }

        Result<IssueAuthor>.Success authorSuccess = (Result<IssueAuthor>.Success)author;

        Result<ProviderUrl> url = ProviderUrl.Create(@event.Url);
        if (url is Result<ProviderUrl>.Failure urlFailure)
        {
            logger.LogWarning(
                "Skipping issue #{IssueNumber}: invalid URL — {Error}",
                @event.IssueNumber,
                urlFailure.Error);
            return;
        }

        Result<ProviderUrl>.Success urlSuccess = (Result<ProviderUrl>.Success)url;

        IssueKind issueKind = IssueKind.FromLabel(@event.IssueKindLabel);

        DetectedIssue detected = DetectedIssue.Detect(
            @event.MonitoredRepositoryId,
            @event.IssueNumber,
            @event.Title,
            @event.Body,
            authorSuccess.Value,
            urlSuccess.Value,
            @event.Labels,
            @event.DetectedAt,
            issueKind);

        db.Set<Issue>().Add(detected);
        await db.SaveChangesAsync(cancellationToken);
    }
}
