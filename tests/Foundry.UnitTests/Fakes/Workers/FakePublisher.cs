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

    private readonly List<PublishRequest> _publishRequests = [];

    private readonly Queue<PublishResult> _publishResults = new();

    public IReadOnlyList<PublishRequest> PublishRequests => _publishRequests;

    /// <summary>Scripts the next Publish results, in order; once they run out every publish has nothing to publish.</summary>
    public FakePublisher WithPublishResults(params PublishResult[] results)
    {
        foreach (PublishResult result in results)
        {
            _publishResults.Enqueue(result);
        }

        return this;
    }

    public Task<PublishResult> PublishAsync(PublishRequest request, CancellationToken cancellationToken)
    {
        _publishRequests.Add(request);
        return Task.FromResult(_publishResults.TryDequeue(out PublishResult? result) ? result : new PublishResult.NothingToPublish());
    }
    private readonly List<HostProbeRequest> _probeRequests = [];

    private readonly Queue<HostProbeOutcome> _probeOutcomes = new();

    public IReadOnlyList<HostProbeRequest> ProbeRequests => _probeRequests;

    /// <summary>Scripts the next probe outcomes, in order; once they run out every probe finds the host reachable.</summary>
    public FakePublisher WithProbeOutcomes(params HostProbeOutcome[] outcomes)
    {
        foreach (HostProbeOutcome outcome in outcomes)
        {
            _probeOutcomes.Enqueue(outcome);
        }

        return this;
    }

    public Task<HostProbeOutcome> ProbeAsync(HostProbeRequest request, CancellationToken cancellationToken)
    {
        _probeRequests.Add(request);
        return Task.FromResult(_probeOutcomes.TryDequeue(out HostProbeOutcome? next) ? next : new HostProbeOutcome.Reachable());
    }
}
