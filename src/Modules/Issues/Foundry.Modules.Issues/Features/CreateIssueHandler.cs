using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Issues.Features;

internal sealed class CreateIssueHandler(
    DbContext db) : IIntegrationEventHandler<IssueDetected>
{
    public async Task HandleAsync(IssueDetected @event, CancellationToken cancellationToken)
    {
        Result<IssueAuthor> authorResult = IssueAuthor.Create(@event.Author);
        if (authorResult is not Result<IssueAuthor>.Success authorSuccess)
        {
            return;
        }

        Result<ProviderUrl> urlResult = ProviderUrl.Create(@event.Url);
        if (urlResult is not Result<ProviderUrl>.Success urlSuccess)
        {
            return;
        }

        DetectedIssue detected = DetectedIssue.Detect(
            @event.MonitoredRepositoryId,
            @event.IssueNumber,
            @event.Title,
            @event.Body,
            authorSuccess.Value,
            urlSuccess.Value,
            @event.Labels,
            @event.DetectedAt);

        db.Set<Issue>().Add(detected);
        await db.SaveChangesAsync(cancellationToken);
    }
}
