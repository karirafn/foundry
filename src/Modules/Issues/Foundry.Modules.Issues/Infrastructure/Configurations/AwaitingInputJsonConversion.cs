using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Workers.Contracts;

using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Foundry.Modules.Issues.Infrastructure.Configurations;

/// <summary>
/// JSON column mappings for the awaiting-input state (ADR 0096): the <see cref="ResumeTarget"/> and
/// <see cref="QuestionPost"/> unions on <c>awaiting_input</c> rows, and the clarification log every issue carries.
/// </summary>
internal static class AwaitingInputJsonConversion
{
    private const string RevisionKind = "revision";
    private const string ContinuationKind = "continuation";
    private const string FreshKind = "fresh";
    private const string RetriageKind = "retriage";
    private const string PendingKind = "pending";
    private const string PostedKind = "posted";
    private const string FailedKind = "failed";

    public static ValueConverter<ResumeTarget, string> ResumeTargetConverter { get; } = new(
        target => SerializeResumeTarget(target),
        json => DeserializeResumeTarget(json));

    public static ValueComparer<ResumeTarget> ResumeTargetComparer { get; } = new(
        (a, b) => a == b,
        target => target.GetHashCode(),
        target => target);

    public static ValueConverter<QuestionPost, string> QuestionPostConverter { get; } = new(
        post => SerializeQuestionPost(post),
        json => DeserializeQuestionPost(json));

    public static ValueComparer<QuestionPost> QuestionPostComparer { get; } = new(
        (a, b) => a == b,
        post => post.GetHashCode(),
        post => post);

    public static ValueConverter<IReadOnlyList<Clarification>, string> ClarificationsConverter { get; } = new(
        log => SerializeClarifications(log),
        json => DeserializeClarifications(json));

    public static ValueComparer<IReadOnlyList<Clarification>> ClarificationsComparer { get; } = new(
        (a, b) => a != null && b != null && a.SequenceEqual(b),
        log => log.Aggregate(0, (hash, round) => HashCode.Combine(hash, round.GetHashCode())),
        log => (IReadOnlyList<Clarification>)log.ToList());

    private static string SerializeResumeTarget(ResumeTarget target)
    {
        ResumeTargetDto dto = target switch
        {
            ResumeTarget.Revision revision => new ResumeTargetDto(RevisionKind, revision.BranchName, revision.PullRequestUrl),
            ResumeTarget.Continuation continuation => new ResumeTargetDto(ContinuationKind, continuation.BranchName, null),
            ResumeTarget.Fresh => new ResumeTargetDto(FreshKind, null, null),
            ResumeTarget.Retriage => new ResumeTargetDto(RetriageKind, null, null),
            _ => throw new UnreachableException($"Unknown ResumeTarget variant: {target.GetType().Name}"),
        };

        return JsonSerializer.Serialize(dto, (JsonSerializerOptions?)null);
    }

    private static ResumeTarget DeserializeResumeTarget(string json)
    {
        ResumeTargetDto dto = JsonSerializer.Deserialize<ResumeTargetDto>(json, (JsonSerializerOptions?)null)
            ?? throw new UnreachableException("Stored resume_target JSON deserialised to null.");

        return dto.Kind switch
        {
            RevisionKind => new ResumeTarget.Revision(Required(dto.BranchName), Required(dto.PullRequestUrl)),
            ContinuationKind => new ResumeTarget.Continuation(Required(dto.BranchName)),
            FreshKind => new ResumeTarget.Fresh(),
            RetriageKind => new ResumeTarget.Retriage(),
            _ => throw new UnreachableException($"Unknown ResumeTarget kind: '{dto.Kind}'"),
        };
    }

    private static string SerializeQuestionPost(QuestionPost post)
    {
        QuestionPostDto dto = post switch
        {
            QuestionPost.Pending => new QuestionPostDto(PendingKind, null, null, null, null, null, null),
            QuestionPost.Posted posted => new QuestionPostDto(
                PostedKind,
                posted.CommentId,
                posted.PostedAt,
                posted.CommentUrl,
                null,
                null,
                null),
            QuestionPost.Failed failed => new QuestionPostDto(
                FailedKind,
                null,
                null,
                null,
                failed.Reason,
                failed.NextAttemptAt,
                failed.Attempts),
            _ => throw new UnreachableException($"Unknown QuestionPost variant: {post.GetType().Name}"),
        };

        return JsonSerializer.Serialize(dto, (JsonSerializerOptions?)null);
    }

    private static QuestionPost DeserializeQuestionPost(string json)
    {
        QuestionPostDto dto = JsonSerializer.Deserialize<QuestionPostDto>(json, (JsonSerializerOptions?)null)
            ?? throw new UnreachableException("Stored question_post JSON deserialised to null.");

        return dto.Kind switch
        {
            PendingKind => new QuestionPost.Pending(),
            PostedKind => new QuestionPost.Posted(Required(dto.CommentId), Required(dto.PostedAt), dto.CommentUrl),
            FailedKind => new QuestionPost.Failed(dto.Reason ?? string.Empty, dto.NextAttemptAt, dto.Attempts ?? 1),
            _ => throw new UnreachableException($"Unknown QuestionPost kind: '{dto.Kind}'"),
        };
    }

    private static string SerializeClarifications(IReadOnlyList<Clarification> log) =>
        JsonSerializer.Serialize(
            log.Select(round => new ClarificationDto(
                [.. round.Questions.Select(q => new QuestionDto(q.Question, q.Context))],
                round.Input,
                round.AskedAt,
                round.AnsweredAt)).ToList(),
            (JsonSerializerOptions?)null);

    private static List<Clarification> DeserializeClarifications(string json) =>
        (JsonSerializer.Deserialize<List<ClarificationDto>>(json, (JsonSerializerOptions?)null) ?? [])
            .Select(dto => new Clarification(
                [.. dto.Questions.Select(q => new InputQuestion(q.Question, q.Context))],
                dto.Input,
                dto.AskedAt,
                dto.AnsweredAt))
            .ToList();

    private static T Required<T>(T? value)
        where T : class =>
        value ?? throw new UnreachableException("A required awaiting-input JSON field is missing.");

    private static T Required<T>(T? value)
        where T : struct =>
        value ?? throw new UnreachableException("A required awaiting-input JSON field is missing.");

    private sealed record ResumeTargetDto(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("branchName")] string? BranchName,
        [property: JsonPropertyName("pullRequestUrl")] string? PullRequestUrl);

    private sealed record QuestionPostDto(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("commentId")] long? CommentId,
        [property: JsonPropertyName("postedAt")] DateTimeOffset? PostedAt,
        [property: JsonPropertyName("commentUrl")] string? CommentUrl,
        [property: JsonPropertyName("reason")] string? Reason,
        [property: JsonPropertyName("nextAttemptAt")] DateTimeOffset? NextAttemptAt,
        [property: JsonPropertyName("attempts")] int? Attempts);

    private sealed record ClarificationDto(
        [property: JsonPropertyName("questions")] IReadOnlyList<QuestionDto> Questions,
        [property: JsonPropertyName("input")] string? Input,
        [property: JsonPropertyName("askedAt")] DateTimeOffset AskedAt,
        [property: JsonPropertyName("answeredAt")] DateTimeOffset? AnsweredAt);

    private sealed record QuestionDto(
        [property: JsonPropertyName("question")] string Question,
        [property: JsonPropertyName("context")] string Context);
}
