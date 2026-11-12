using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Publisher;

namespace Foundry.IntegrationTests.Smoke;

/// <summary>A Publisher with no Docker behind it: every Prepare succeeds with a fixed base, and every Publish pushes one commit.</summary>
internal sealed class PreparedPublisher : IPublisher
{
    private static readonly CommitSha Head = CommitSha.From("0123456789abcdef0123456789abcdef01234567");

    public Task<PrepareOutcome> PrepareAsync(PrepareRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Task.FromResult<PrepareOutcome>(
            new PrepareOutcome.Prepared(new RunBase(Head, request.RunBranch is null ? null : Head)));
    }

    public Task<PublishResult> PublishAsync(PublishRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Task.FromResult<PublishResult>(new PublishResult.Published(Head, 1, ["feat: smoke-tested feature"], []));
    }
}
