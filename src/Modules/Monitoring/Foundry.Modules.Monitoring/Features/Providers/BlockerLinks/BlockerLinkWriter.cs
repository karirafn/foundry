using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.CredentialResolution;
using Foundry.Modules.Monitoring.Features.Providers.Comments;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Monitoring.Features.Providers.BlockerLinks;

using ResolvedProvider = (IIssueProvider Provider, MonitoredRepository Repo);

/// <summary>
/// Writes blocker links and the triage comment to a monitored repository's provider. Failures carry
/// <see cref="BlockerLinkErrors"/> or <see cref="CommentPostErrors"/> codes so the caller owns the retry policy.
/// </summary>
internal sealed class BlockerLinkWriter(
    DbContext dbContext,
    IIssueProviderFactory providerFactory,
    ICredentialResolver credentialResolver,
    PostedCommentRegistry postedCommentRegistry,
    TimeProvider timeProvider)
{
    public async Task<Result> AddBlockerAsync(
        MonitoredRepositoryId repositoryId,
        int issueNumber,
        int blockerNumber,
        CancellationToken cancellationToken)
    {
        Result<ResolvedProvider> resolved = await ResolveAsync(repositoryId, cancellationToken);

        if (resolved is not Result<ResolvedProvider>.Success success)
        {
            return Result.Fail(BlockerLinkErrors.Permanent(ResolutionMessage(resolved)));
        }

        try
        {
            return await success.Value.Provider.AddBlockerAsync(
                success.Value.Repo.Slug, issueNumber, blockerNumber, cancellationToken);
        }
        catch (Exception ex) when (BlockerLinkErrors.IsTransportFailure(ex, cancellationToken))
        {
            return Result.Fail(BlockerLinkErrors.Transient(UnreachableMessage(repositoryId, ex)));
        }
    }

    public async Task<Result> PostIssueCommentAsync(
        MonitoredRepositoryId repositoryId,
        int issueNumber,
        string body,
        CancellationToken cancellationToken)
    {
        Result<ResolvedProvider> resolved = await ResolveAsync(repositoryId, cancellationToken);

        if (resolved is not Result<ResolvedProvider>.Success success)
        {
            return Result.Fail(CommentPostErrors.Permanent(ResolutionMessage(resolved)));
        }

        Result<ProviderCommentId> posted;
        try
        {
            posted = await success.Value.Provider.PostCommentAsync(
                success.Value.Repo.Slug, new CommentThread.Issue(issueNumber), body, cancellationToken);
        }
        catch (Exception ex) when (BlockerLinkErrors.IsTransportFailure(ex, cancellationToken))
        {
            return Result.Fail(CommentPostErrors.Transient(UnreachableMessage(repositoryId, ex)));
        }

        if (posted is not Result<ProviderCommentId>.Success postedSuccess)
        {
            return Result.Fail(((Result<ProviderCommentId>.Failure)posted).Error);
        }

        // Saved at once so a later failure in the caller cannot leave the comment unregistered and read back
        // as feedback.
        postedCommentRegistry.Record(repositoryId, postedSuccess.Value, timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }

    private static string ResolutionMessage(Result<ResolvedProvider> resolved) =>
        ((Result<ResolvedProvider>.Failure)resolved).Error.Message;

    private static string UnreachableMessage(MonitoredRepositoryId repositoryId, Exception exception) =>
        $"The provider for repository '{repositoryId.Value}' could not be reached: {exception.GetType().Name}.";

    private Task<Result<ResolvedProvider>> ResolveAsync(
        MonitoredRepositoryId repositoryId,
        CancellationToken cancellationToken) =>
        RepositoryProviderResolution.ResolveAsync(
            dbContext, credentialResolver, providerFactory, repositoryId, cancellationToken);
}
