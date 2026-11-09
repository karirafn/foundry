using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Shared;

namespace Foundry.Testing;

/// <summary>
/// <see cref="IFailingJobLogQueries"/> stub that answers <see cref="CiJobLog.NotACiJob"/> unless a per-check result
/// is configured, and records each check it was asked about.
/// </summary>
public sealed class StubFailingJobLogQueries : IFailingJobLogQueries
{
    private readonly Dictionary<string, Func<Result<CiJobLog>>> _resultsByCheckName = new(StringComparer.Ordinal);
    private readonly List<FailingCheck> _requestedChecks = [];

    public IReadOnlyList<FailingCheck> RequestedChecks => _requestedChecks;

    public StubFailingJobLogQueries WithLog(string checkName, string text)
    {
        _resultsByCheckName[checkName] = () => Result<CiJobLog>.Ok(new CiJobLog.Available(text));
        return this;
    }

    public StubFailingJobLogQueries WithFailure(string checkName)
    {
        _resultsByCheckName[checkName] = () => Result<CiJobLog>.Fail(
            new Error("Stub.JobLogReadFailed", $"Job log read failed for check {checkName}."));
        return this;
    }

    public StubFailingJobLogQueries WithException(string checkName)
    {
        _resultsByCheckName[checkName] = () => throw new HttpRequestException($"Connection reset reading {checkName}.");
        return this;
    }

    public Task<Result<CiJobLog>> GetFailingJobLogAsync(
        MonitoredRepositoryId repositoryId,
        FailingCheck check,
        CancellationToken cancellationToken)
    {
        _requestedChecks.Add(check);

        Result<CiJobLog> result = _resultsByCheckName.TryGetValue(check.Name, out Func<Result<CiJobLog>>? configured)
            ? configured()
            : Result<CiJobLog>.Ok(new CiJobLog.NotACiJob());

        return Task.FromResult(result);
    }
}
