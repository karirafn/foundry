using System.Diagnostics;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Commands;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.CredentialResolution;
using Foundry.Modules.Monitoring.Features.Providers.Comments;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Monitoring.Features.Providers.Publishing;

internal sealed class RunPublishingCommands(
    DbContext dbContext,
    IIssueProviderFactory providerFactory,
    ICredentialResolver credentialResolver,
    PostedCommentRegistry postedCommentRegistry,
    TimeProvider timeProvider) : IRunPublishingCommands
{
    public Task<Result<string>> OpenPullRequestAsync(
        MonitoredRepositoryId repositoryId,
        string branchName,
        string title,
        string body,
        CancellationToken cancellationToken) =>
        CallProviderAsync(
            repositoryId,
            (provider, repo) => provider.CreatePullRequestAsync(repo.Slug, branchName, title, body, cancellationToken),
            cancellationToken);

    public async Task<Result> PostRunSummaryAsync(
        MonitoredRepositoryId repositoryId,
        string pullRequestUrl,
        string body,
        CancellationToken cancellationToken)
    {
        Result<ProviderCommentId> posted = await CallProviderAsync(
            repositoryId,
            (provider, repo) => provider.PostCommentAsync(
                repo.Slug, new CommentThread.PullRequest(pullRequestUrl), body, cancellationToken),
            cancellationToken);

        if (posted is not Result<ProviderCommentId>.Success success)
        {
            return Result.Fail(((Result<ProviderCommentId>.Failure)posted).Error);
        }

        // Saved at once so a later failure in the caller cannot leave the comment unregistered and read back
        // as review feedback.
        postedCommentRegistry.Record(repositoryId, success.Value, timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }

    public Task<Result<CommitIdentity>> GetCommitIdentityAsync(
        MonitoredRepositoryId repositoryId,
        CancellationToken cancellationToken) =>
        CallProviderAsync(
            repositoryId,
            (provider, _) => provider.GetAuthenticatedIdentityAsync(cancellationToken),
            cancellationToken);

    public Task<Result<bool>> IsAncestorOfBranchAsync(
        MonitoredRepositoryId repositoryId,
        string ancestorRef,
        string branchName,
        CancellationToken cancellationToken) =>
        CallProviderAsync(
            repositoryId,
            (provider, repo) => provider.IsAncestorAsync(repo.Slug, ancestorRef, branchName, cancellationToken),
            cancellationToken);

    private async Task<Result<T>> CallProviderAsync<T>(
        MonitoredRepositoryId repositoryId,
        Func<IIssueProvider, MonitoredRepository, Task<Result<T>>> call,
        CancellationToken cancellationToken)
        where T : notnull
    {
        Result<(IIssueProvider Provider, MonitoredRepository Repo)> resolved =
            await RepositoryProviderResolution.ResolveAsync(
                dbContext, credentialResolver, providerFactory, repositoryId, cancellationToken);

        if (resolved is not Result<(IIssueProvider, MonitoredRepository)>.Success resolvedSuccess)
        {
            // An unknown repository or an unusable credential needs an operator, so no retry can fix it.
            Error error = ((Result<(IIssueProvider, MonitoredRepository)>.Failure)resolved).Error;
            return Result<T>.Fail(RunPublishingErrors.Permanent(error.Message));
        }

        (IIssueProvider provider, MonitoredRepository repo) = resolvedSuccess.Value;

        Result<T> result;
        try
        {
            result = await call(provider, repo);
        }
        catch (Exception ex) when (ex is HttpRequestException
            || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return Result<T>.Fail(RunPublishingErrors.Transient(
                $"The provider for repository '{repositoryId.Value}' could not be reached: {ex.GetType().Name}."));
        }

        return result switch
        {
            Result<T>.Success => result,
            Result<T>.Failure failure => Result<T>.Fail(Classify(failure.Error)),
            _ => throw new UnreachableException(),
        };
    }

    /// <summary>
    /// Maps every provider failure onto the contract's codes. Errors the provider already classified keep their
    /// code; a comment-post failure keeps its transient/permanent split. A provider read that failed without a
    /// classification (the default-branch lookup) is permanent only when the provider reported the resource
    /// missing, and transient otherwise: a bounded retry of an unknown failure is cheaper than failing a run
    /// whose work is ready to publish.
    /// </summary>
    private static Error Classify(Error error) => error.Code switch
    {
        RunPublishingErrors.TransientCode or RunPublishingErrors.PermanentCode or RunPublishingErrors.RefNotFoundCode =>
            error,
        CommentPostErrors.TransientCode => RunPublishingErrors.Transient(error.Message),
        CommentPostErrors.PermanentCode => RunPublishingErrors.Permanent(error.Message),
        _ when error.Kind is ErrorKind.NotFound => RunPublishingErrors.Permanent(error.Message),
        _ => RunPublishingErrors.Transient(error.Message),
    };
}
