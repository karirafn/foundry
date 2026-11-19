using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Infrastructure.RunVerdictValueConvertersTests;

public sealed class VerdictQuestions
{
    private static readonly ValueConverter Converter = ReadConverter();

    private static ValueConverter ReadConverter()
    {
        using FoundryDbContext dbContext = new(
            new DbContextOptionsBuilder<FoundryDbContext>().UseSqlite("Data Source=:memory:").Options);

        return dbContext.Model.FindEntityType(typeof(WorkerRun))!
            .FindProperty(nameof(WorkerRun.VerdictQuestions))!
            .GetValueConverter()!;
    }

    private static IReadOnlyList<InputQuestion> Deserialize(string json) =>
        (IReadOnlyList<InputQuestion>)Converter.ConvertFromProvider(json)!;

    [Fact]
    public void AStructuredQuestion_RoundTripsEveryField()
    {
        // Arrange
        IReadOnlyList<InputQuestion> questions =
        [
            StructuredQuestionBuilder.Build(
                options: ["SQLite", "Postgres", "Both"],
                recommendedOption: "c",
                evidence: ["src/Db.cs:12 opens SQLite", "appsettings.json:4 names Postgres"]),
        ];

        // Act
        IReadOnlyList<InputQuestion> read = Deserialize((string)Converter.ConvertToProvider(questions)!);

        // Assert
        read.ShouldBe(questions);
    }

    // The shape worker_runs.verdict_questions held before questions were structured, as the converter wrote it.
    [Fact]
    public void AQuestionStoredBeforeQuestionsWereStructured_ReadsAsLegacy()
    {
        // Act
        IReadOnlyList<InputQuestion> read = Deserialize(
            """[{"question":"Which database?","context":"The issue names both.","recommendation":"Postgres."}]""");

        // Assert
        read.ShouldBe([new InputQuestion.Legacy("Which database?", "The issue names both.", "Postgres.")]);
    }

    [Fact]
    public void AQuestionStoredWithoutARecommendation_ReadsAsLegacyWithANullRecommendation()
    {
        // Act
        IReadOnlyList<InputQuestion> read = Deserialize("""[{"question":"Which database?","context":"The issue names both."}]""");

        // Assert
        read.ShouldBe([new InputQuestion.Legacy("Which database?", "The issue names both.", null)]);
    }
}
