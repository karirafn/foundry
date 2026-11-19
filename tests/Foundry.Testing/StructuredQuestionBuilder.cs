using Foundry.Modules.Workers.Contracts;
using Foundry.Testing;

namespace Foundry.Testing;

public static class StructuredQuestionBuilder
{
    public static InputQuestion.Structured Build(
        string question = "Which database?",
        string problem = "Two are configured.",
        string impact = "Migrations target the wrong one.",
        IReadOnlyList<string>? options = null,
        string recommendedOption = "b",
        string recommendationReason = "Production runs it.",
        IReadOnlyList<string>? evidence = null) =>
        InputQuestion.Structured.Create(
            question,
            problem,
            impact,
            options ?? ["SQLite", "Postgres"],
            recommendedOption,
            recommendationReason,
            evidence ?? ["src/Db.cs:12 opens SQLite"]).ValueOrThrow();
}
