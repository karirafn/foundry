using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Publisher;

namespace Foundry.UnitTests.Fakes.Workers;

/// <summary>Records each Prepare request and answers with a scripted outcome (by default, a prepared base).</summary>
internal sealed class FakePublisher(PrepareOutcome? outcome = null) : IPublisher
{
    public static readonly CommitSha DefaultBranchHead = CommitSha.From("1111111111111111111111111111111111111111");

    public static readonly CommitSha RunBranchHead = CommitSha.From("2222222222222222222222222222222222222222");

    private readonly List<PrepareRequest> _requests = [];

    public IReadOnlyList<PrepareRequest> Requests => _requests;

    public Task<PrepareOutcome> PrepareAsync(PrepareRequest request, CancellationToken cancellationToken)
    {
        _requests.Add(request);

        PrepareOutcome prepared = new PrepareOutcome.Prepared(
            new RunBase(DefaultBranchHead, request.RunBranch is null ? null : RunBranchHead));

        return Task.FromResult(outcome ?? prepared);
    }
}
