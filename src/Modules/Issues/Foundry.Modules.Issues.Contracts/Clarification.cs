using System.Text.Json.Serialization;

using Foundry.Modules.Workers.Contracts;

namespace Foundry.Modules.Issues.Contracts;

/// <summary>
/// One round on an issue's append-only clarification log (ADR 0096): the questions a run asked, the input that
/// answered them, and when each happened. An open round has neither input nor an answer time; a closed round has both.
/// </summary>
public sealed record Clarification
{
    [JsonConstructor]
    public Clarification(
        IReadOnlyList<InputQuestion> questions,
        string? input,
        DateTimeOffset askedAt,
        DateTimeOffset? answeredAt)
    {
        ArgumentNullException.ThrowIfNull(questions);

        if (questions.Count == 0)
        {
            throw new ArgumentException("A clarification asks at least one question.", nameof(questions));
        }

        if (input is null != answeredAt is null)
        {
            throw new ArgumentException("A clarification is answered with both its input and its answer time, or neither.", nameof(input));
        }

        Questions = [.. questions];
        Input = input;
        AskedAt = askedAt;
        AnsweredAt = answeredAt;
    }

    public IReadOnlyList<InputQuestion> Questions { get; }

    /// <summary>What answered the questions; <see langword="null"/> while the round is open.</summary>
    public string? Input { get; }

    public DateTimeOffset AskedAt { get; }

    public DateTimeOffset? AnsweredAt { get; }

    [JsonIgnore]
    public bool IsOpen => AnsweredAt is null;

    public static Clarification Ask(IReadOnlyList<InputQuestion> questions, DateTimeOffset askedAt) =>
        new(questions, input: null, askedAt, answeredAt: null);

    public Clarification Answer(string input, DateTimeOffset answeredAt)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (!IsOpen)
        {
            throw new InvalidOperationException("The clarification round is already answered.");
        }

        return new Clarification(Questions, input, AskedAt, answeredAt);
    }

    public bool Equals(Clarification? other) =>
        other is not null
        && Questions.SequenceEqual(other.Questions)
        && Input == other.Input
        && AskedAt == other.AskedAt
        && AnsweredAt == other.AnsweredAt;

    public override int GetHashCode() => HashCode.Combine(Questions.Count, Input, AskedAt, AnsweredAt);
}
