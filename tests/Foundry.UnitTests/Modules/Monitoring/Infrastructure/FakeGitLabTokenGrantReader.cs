using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Accounts.Tokens;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Infrastructure.GitLab;
using Foundry.Shared;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure;

/// <summary>
/// Answers the GitLab grant reads with a fixed token record and project id. By default the token is a
/// fine-grained one holding exactly what Foundry requires on <see cref="DefaultProjectId"/>, which every
/// repository resolves to.
/// </summary>
internal sealed class FakeGitLabTokenGrantReader : IGitLabTokenGrantReader
{
    internal const int DefaultProjectId = 42;

    internal Result<GitLabTokenSelf> Self { get; init; } =
        Result<GitLabTokenSelf>.Ok(new GitLabTokenSelf.FineGrained(ConformingGrant(DefaultProjectId), new TokenExpiry.Never()));

    internal Result<int> ProjectId { get; init; } = Result<int>.Ok(DefaultProjectId);

    internal int SelfReads { get; private set; }

    internal static GitLabTokenGrant ConformingGrant(params long[] projectIds) =>
        GitLabTokenGrant.Create(
        [
            .. projectIds.Select(id => new GitLabTokenGrantEntry(
                GitLabTokenGrantEntry.SelectedMemberships,
                TokenPermissions.GitLabGrantRequirements.Project,
                id,
                GroupId: null)),
            new GitLabTokenGrantEntry(
                GitLabTokenGrantEntry.User,
                TokenPermissions.GitLabGrantRequirements.User,
                ProjectId: null,
                GroupId: null),
        ]);

    internal static GitLabTokenGrant ConformingAllMembershipsGrant() =>
        GitLabTokenGrant.Create(
        [
            new GitLabTokenGrantEntry(
                GitLabTokenGrantEntry.AllMemberships,
                TokenPermissions.GitLabGrantRequirements.Project,
                ProjectId: null,
                GroupId: null),
            new GitLabTokenGrantEntry(
                GitLabTokenGrantEntry.User,
                [.. TokenPermissions.GitLabGrantRequirements.User, .. TokenPermissions.GitLabGrantRequirements.UserWithAllMemberships],
                ProjectId: null,
                GroupId: null),
        ]);

    public Task<Result<GitLabTokenSelf>> ReadTokenSelfAsync(
        Uri apiBaseUrl,
        string token,
        CancellationToken cancellationToken)
    {
        SelfReads++;
        return Task.FromResult(Self);
    }

    public Task<Result<int>> ResolveProjectIdAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string token,
        CancellationToken cancellationToken) =>
        Task.FromResult(ProjectId);
}
