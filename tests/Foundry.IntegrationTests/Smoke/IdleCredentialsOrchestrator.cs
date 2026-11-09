using System.Runtime.CompilerServices;

using Foundry.Modules.Credentials.Features.CreditProbe;
using Foundry.Modules.Credentials.Features.Login;
using Foundry.Modules.Credentials.Features.SessionCheck;
using Foundry.Modules.Credentials.Features.Usage;
using Foundry.Modules.Credentials.Infrastructure.Orchestration;
using Foundry.Shared;

namespace Foundry.IntegrationTests.Smoke;

/// <summary>
/// The credential helpers' container work with no Docker behind it: there are no transient containers,
/// and every helper run fails at the orchestrator level, which the credential services treat as a
/// transient infrastructure failure rather than a reason to stop dispatch.
/// </summary>
internal sealed class IdleCredentialsOrchestrator : ICredentialsOrchestrator
{
    private static readonly Error NoDocker = new("Smoke.NoDocker", "The smoke test runs without a Docker daemon.");

    public Task<Result<string>> StartLoginContainerAsync(LoginContainerSpec spec, CancellationToken cancellationToken)
        => Task.FromResult(Result<string>.Fail(NoDocker));

    public Task DeliverLoginCodeAsync(string containerId, string code, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task<Result<AccountIdentity>> GetCredentialVolumeAuthStatusAsync(CancellationToken cancellationToken)
        => Task.FromResult(Result<AccountIdentity>.Fail(NoDocker));

    public async IAsyncEnumerable<string> StreamLogsAsync(string containerId, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        yield break;
    }

    public Task StopContainerAsync(string containerId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task RemoveContainerAsync(string containerId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<IReadOnlyList<string>> ListTransientContainersAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<string>>([]);

    public Task<IReadOnlyList<string>> ListExitedTransientContainersAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<string>>([]);

    public Task<Result<string>> RunCreditProbeAsync(CreditProbeSpec spec, CancellationToken cancellationToken)
        => Task.FromResult(Result<string>.Fail(NoDocker));

    public Task<Result<string>> RunUsageReportAsync(UsageReportSpec spec, CancellationToken cancellationToken)
        => Task.FromResult(Result<string>.Fail(NoDocker));

    public Task<Result<OAuthSessionStatus>> CheckOAuthSessionAsync(CancellationToken cancellationToken)
        => Task.FromResult(Result<OAuthSessionStatus>.Fail(NoDocker));

    public Task SeedOnboardingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task EnsureCredentialVolumeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<Result<string>> ReadOAuthAccessTokenAsync(CancellationToken cancellationToken)
        => Task.FromResult(Result<string>.Fail(NoDocker));
}
