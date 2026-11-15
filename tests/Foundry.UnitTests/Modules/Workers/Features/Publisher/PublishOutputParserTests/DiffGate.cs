using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Publisher;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Publisher.PublishOutputParserTests;

public sealed class DiffGate
{
    private const string Sha = "3333333333333333333333333333333333333333";
    private const string FindingSha = "4444444444444444444444444444444444444444";

    [Fact]
    public void ASecretDetectedResult_YieldsEveryFinding()
    {
        // Arrange
        string log = $"""
            The secret scan found 2 possible secret(s); nothing is pushed
            FOUNDRY_SECRET_FINDING commit={FindingSha} line=14 rule=aws-access-token path=src/Api/appsettings.json
            2026-11-11T12:00:00.123Z FOUNDRY_SECRET_FINDING commit={FindingSha} line=3 rule=generic-api-key path=scripts/deploy plan.sh
            FOUNDRY_PUBLISH result=secret_detected findings=2
            """;

        // Act
        PublishResult result = PublishOutputParser.Parse(0, log);

        // Assert
        result.ShouldBeOfType<PublishResult.SecretDetected>().Findings.ShouldBe(
        [
            new SecretFinding("src/Api/appsettings.json", 14, CommitSha.From(FindingSha), "aws-access-token"),
            new SecretFinding("scripts/deploy plan.sh", 3, CommitSha.From(FindingSha), "generic-api-key"),
        ]);
    }

    [Fact]
    public void ASecretDetectedResult_WhoseFindingCountDoesNotMatchItsLines_IsAPermanentFailure()
    {
        // Arrange
        string log = $"""
            FOUNDRY_SECRET_FINDING commit={FindingSha} line=14 rule=aws-access-token path=a.json
            FOUNDRY_PUBLISH result=secret_detected findings=2
            """;

        // Act
        PublishResult result = PublishOutputParser.Parse(0, log);

        // Assert
        result.ShouldBeOfType<PublishResult.Failed>().Transient.ShouldBeFalse();
    }

    [Fact]
    public void ASecretDetectedResult_WithNoFindings_IsAPermanentFailure()
    {
        // Act
        PublishResult result = PublishOutputParser.Parse(0, "FOUNDRY_PUBLISH result=secret_detected findings=0\n");

        // Assert
        result.ShouldBeOfType<PublishResult.Failed>().Transient.ShouldBeFalse();
    }

    [Fact]
    public void AFinding_HasNoMemberForAMatchedValue()
    {
        // Act
        string[] members = [.. typeof(SecretFinding).GetProperties().Select(property => property.Name)];

        // Assert
        members.ShouldBe(["Path", "Line", "Commit", "RuleId"], ignoreOrder: true);
    }

    [Fact]
    public void APublishedResult_YieldsEveryRevertedFile()
    {
        // Arrange
        string log = $"""
            FOUNDRY_RUN_COMMIT {Sha} feat: add assets
            FOUNDRY_REVERTED_FILE reason=oversized size=50540134 path=assets/demo.mp4
            FOUNDRY_REVERTED_FILE reason=binary size=3250585 path=tools/my protoc.exe
            FOUNDRY_PUBLISH result=published head={Sha} commits=2
            """;

        // Act
        PublishResult result = PublishOutputParser.Parse(0, log);

        // Assert
        result.ShouldBeOfType<PublishResult.Published>().RevertedFiles.ShouldBe(
        [
            new RevertedFile("assets/demo.mp4", RevertedFileReason.Oversized, 50540134),
            new RevertedFile("tools/my protoc.exe", RevertedFileReason.Binary, 3250585),
        ]);
    }

    [Fact]
    public void APublishedResult_WithoutRevertedFiles_HasNone()
    {
        // Act
        PublishResult result = PublishOutputParser.Parse(0, $"FOUNDRY_PUBLISH result=published head={Sha} commits=1\n");

        // Assert
        result.ShouldBeOfType<PublishResult.Published>().RevertedFiles.ShouldBeEmpty();
    }

    [Fact]
    public void ADivergedResult_YieldsEveryRevertedFile()
    {
        // Arrange
        string log = $"""
            FOUNDRY_REVERTED_FILE reason=binary size=12 path=logo.png
            FOUNDRY_PUBLISH result=diverged branch=feat/42-x side_branch=feat/42-x-run-abc head={Sha}
            """;

        // Act
        PublishResult result = PublishOutputParser.Parse(0, log);

        // Assert
        result.ShouldBeOfType<PublishResult.Diverged>().RevertedFiles.ShouldBe(
            [new RevertedFile("logo.png", RevertedFileReason.Binary, 12)]);
    }

    [Fact]
    public void ARevertedFileLineWithAnUnknownReason_IsSkippedWithoutFailingThePushedResult()
    {
        // Arrange
        string log = $"""
            FOUNDRY_REVERTED_FILE reason=huge size=12 path=logo.png
            FOUNDRY_PUBLISH result=published head={Sha} commits=1
            """;

        // Act
        PublishResult result = PublishOutputParser.Parse(0, log);

        // Assert
        result.ShouldBeOfType<PublishResult.Published>().RevertedFiles.ShouldBeEmpty();
    }

    [Fact]
    public void APublishedResult_YieldsEveryGenericFinding()
    {
        // Arrange
        string log = $"""
            FOUNDRY_RUN_COMMIT {Sha} feat: add store
            FOUNDRY_GENERIC_FINDING commit={FindingSha} line=7 rule=generic-api-key path=src/store.ts
            2026-11-14T12:00:00.123Z FOUNDRY_GENERIC_FINDING commit={FindingSha} line=2 rule=generic-api-key path=docs/read me.md
            FOUNDRY_PUBLISH result=published head={Sha} commits=1
            """;

        // Act
        PublishResult result = PublishOutputParser.Parse(0, log);

        // Assert
        result.ShouldBeOfType<PublishResult.Published>().GenericFindings.ShouldBe(
        [
            new SecretFinding("src/store.ts", 7, CommitSha.From(FindingSha), "generic-api-key"),
            new SecretFinding("docs/read me.md", 2, CommitSha.From(FindingSha), "generic-api-key"),
        ]);
    }

    [Fact]
    public void APublishedResult_WithoutGenericFindings_HasNone()
    {
        // Act
        PublishResult result = PublishOutputParser.Parse(0, $"FOUNDRY_PUBLISH result=published head={Sha} commits=1\n");

        // Assert
        result.ShouldBeOfType<PublishResult.Published>().GenericFindings.ShouldBeEmpty();
    }

    [Fact]
    public void ADivergedResult_YieldsEveryGenericFinding()
    {
        // Arrange
        string log = $"""
            FOUNDRY_GENERIC_FINDING commit={FindingSha} line=1 rule=generic-api-key path=shared.txt
            FOUNDRY_PUBLISH result=diverged branch=feat/42-x side_branch=feat/42-x-run-abc head={Sha}
            """;

        // Act
        PublishResult result = PublishOutputParser.Parse(0, log);

        // Assert
        result.ShouldBeOfType<PublishResult.Diverged>().GenericFindings.ShouldBe(
            [new SecretFinding("shared.txt", 1, CommitSha.From(FindingSha), "generic-api-key")]);
    }

    [Fact]
    public void AnUnreadableGenericFindingLine_IsSkippedWithoutFailingThePushedResult()
    {
        // Arrange
        string log = $"""
            FOUNDRY_GENERIC_FINDING commit={FindingSha} line=0 rule=generic-api-key path=a.ts
            FOUNDRY_GENERIC_FINDING commit=xyz line=3 rule=generic-api-key path=b.ts
            FOUNDRY_GENERIC_FINDING commit={FindingSha} line=4 rule=generic-api-key path=c.ts
            FOUNDRY_PUBLISH result=published head={Sha} commits=1
            """;

        // Act
        PublishResult result = PublishOutputParser.Parse(0, log);

        // Assert
        result.ShouldBeOfType<PublishResult.Published>().GenericFindings.ShouldBe(
            [new SecretFinding("c.ts", 4, CommitSha.From(FindingSha), "generic-api-key")]);
    }

    [Fact]
    public void ASecretDetectedResult_IgnoresGenericFindingLines()
    {
        // Arrange
        string log = $"""
            FOUNDRY_GENERIC_FINDING commit={FindingSha} line=2 rule=generic-api-key path=b.ts
            FOUNDRY_SECRET_FINDING commit={FindingSha} line=14 rule=github-pat path=a.env
            FOUNDRY_PUBLISH result=secret_detected findings=1
            """;

        // Act
        PublishResult result = PublishOutputParser.Parse(0, log);

        // Assert
        result.ShouldBeOfType<PublishResult.SecretDetected>().Findings.ShouldBe(
            [new SecretFinding("a.env", 14, CommitSha.From(FindingSha), "github-pat")]);
    }
}
