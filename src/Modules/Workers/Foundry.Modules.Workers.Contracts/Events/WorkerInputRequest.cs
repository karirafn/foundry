using System.Text.Json.Serialization;

using Foundry.Shared;

namespace Foundry.Modules.Workers.Contracts;

/// <summary>
/// What a run that stopped to ask a person left behind (ADR 0096): its questions, and whether its branch carries
/// commits. Together with the event's pull request URL these are the facts the resume target is derived from.
/// </summary>
public sealed record WorkerInputRequest(IReadOnlyList<InputQuestion> Questions, bool BranchHasCommits);

/// <summary>
/// One question a run asks a person. A reply that leaves the question unaddressed accepts the recommendation.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(Structured), StructuredDiscriminator)]
[JsonDerivedType(typeof(Legacy), LegacyDiscriminator)]
public abstract record InputQuestion
{
    public const string StructuredDiscriminator = "structured";
    public const string LegacyDiscriminator = "legacy";

    private InputQuestion()
    {
    }

    public abstract string Question { get; }

    /// <summary>
    /// A question with lettered options, one of them recommended with its reason, and the evidence behind it. Letters
    /// follow the options' positions: <c>a</c> for the first.
    /// </summary>
    public sealed record Structured : InputQuestion
    {
        public const int MinOptions = 2;
        public const int MaxOptions = 4;
        public const int MaxEvidence = 5;

        [JsonConstructor]
        private Structured(
            string question,
            string problem,
            string impact,
            IReadOnlyList<string> options,
            string recommendedOption,
            string recommendationReason,
            IReadOnlyList<string> evidence)
        {
            if (Validate(question, problem, impact, options, recommendedOption, recommendationReason, evidence) is { } error)
            {
                throw new ArgumentException(error.Message);
            }

            Question = question;
            Problem = problem;
            Impact = impact;
            Options = [.. options];
            RecommendedOption = recommendedOption;
            RecommendationReason = recommendationReason;
            Evidence = [.. evidence];
        }

        public override string Question { get; }

        /// <summary>What is wrong or undecided.</summary>
        public string Problem { get; }

        /// <summary>What goes wrong if it is left as it is.</summary>
        public string Impact { get; }

        public IReadOnlyList<string> Options { get; }

        /// <summary>The letter of the recommended option.</summary>
        public string RecommendedOption { get; }

        public string RecommendationReason { get; }

        /// <summary><c>file:line</c> references and the facts read from them.</summary>
        public IReadOnlyList<string> Evidence { get; }

        [JsonIgnore]
        public string RecommendedOptionText => Options[RecommendedOption[0] - 'a'];

        public static Result<Structured> Create(
            string question,
            string problem,
            string impact,
            IReadOnlyList<string> options,
            string recommendedOption,
            string recommendationReason,
            IReadOnlyList<string> evidence)
        {
            if (Validate(question, problem, impact, options, recommendedOption, recommendationReason, evidence) is { } error)
            {
                return Result<Structured>.Fail(error);
            }

            return new Structured(question, problem, impact, options, recommendedOption, recommendationReason, evidence);
        }

        /// <summary>The letter of the option at <paramref name="index"/>: <c>a</c> for the first.</summary>
        public static string LetterOf(int index) => ((char)('a' + index)).ToString();

        public bool Equals(Structured? other) =>
            other is not null
            && Question == other.Question
            && Problem == other.Problem
            && Impact == other.Impact
            && Options.SequenceEqual(other.Options)
            && RecommendedOption == other.RecommendedOption
            && RecommendationReason == other.RecommendationReason
            && Evidence.SequenceEqual(other.Evidence);

        public override int GetHashCode() =>
            HashCode.Combine(Question, Problem, Impact, Options.Count, RecommendedOption, RecommendationReason, Evidence.Count);

        private static Error? Validate(
            string question,
            string problem,
            string impact,
            IReadOnlyList<string> options,
            string recommendedOption,
            string recommendationReason,
            IReadOnlyList<string> evidence)
        {
            if (string.IsNullOrWhiteSpace(question)
                || string.IsNullOrWhiteSpace(problem)
                || string.IsNullOrWhiteSpace(impact)
                || string.IsNullOrWhiteSpace(recommendationReason))
            {
                return new Error("InputQuestion.MissingText", "A structured question needs its question, problem, impact and recommendation reason.");
            }

            if (options is null || options.Count is < MinOptions or > MaxOptions || options.Any(string.IsNullOrWhiteSpace))
            {
                return new Error("InputQuestion.Options", $"A structured question offers {MinOptions} to {MaxOptions} non-empty options.");
            }

            if (Enumerable.Range(0, options.Count).All(index => LetterOf(index) != recommendedOption))
            {
                return new Error("InputQuestion.RecommendedOption", $"The recommended option '{recommendedOption}' names none of the {options.Count} options.");
            }

            if (evidence is null || evidence.Count > MaxEvidence || evidence.Any(string.IsNullOrWhiteSpace))
            {
                return new Error("InputQuestion.Evidence", $"A structured question carries at most {MaxEvidence} non-empty evidence items.");
            }

            return null;
        }
    }

    /// <summary>
    /// A question stored before runs returned structured questions: free-text context and recommendation.
    /// <see cref="Recommendation"/> is null only for a question stored before runs returned recommendations.
    /// Only stored rows produce one.
    /// </summary>
    public sealed record Legacy : InputQuestion
    {
        [JsonConstructor]
        public Legacy(string question, string context, string? recommendation)
        {
            Question = question;
            Context = context;
            Recommendation = recommendation;
        }

        public override string Question { get; }

        public string Context { get; }

        public string? Recommendation { get; }
    }
}
