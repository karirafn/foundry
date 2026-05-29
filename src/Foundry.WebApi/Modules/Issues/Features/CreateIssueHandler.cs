using Foundry.Modules.Monitoring.Contracts;
using Foundry.WebApi.Modules.Issues.Domain;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

namespace Foundry.WebApi.Modules.Issues.Features;

internal sealed class CreateIssueHandler(FoundryDbContext db) : IIntegrationEventHandler<IssueDetected>
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
