using System.Text.Json.Nodes;

using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Outcome;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Outcome.RunPullRequestTextReaderTests;

public sealed class Read
{
    private static JsonObject Json(string json) => JsonNode.Parse(json)!.AsObject();

    [Fact]
    public void WhenTheVerdictCarriesEveryField_KeepsThemAsWritten()
    {
        // Act
        RunPullRequestText? text = RunPullRequestTextReader.Read(Json(
            """{"outcome":"done","pullRequest":{"summary":"add retry","cause":"It timed out.","changes":["Adds retry."],"verification":["Ran it."],"attention":["Check staging."],"notes":"None."}}"""));

        // Assert
        text.ShouldBe(new RunPullRequestText(
            "add retry",
            new RunPullRequestDescription("It timed out.", ["Adds retry."], ["Ran it."], ["Check staging."], "None.")));
    }

    [Fact]
    public void WhenAFieldHasTheWrongType_ItReadsAsAbsent()
    {
        // Act
        RunPullRequestText? text = RunPullRequestTextReader.Read(Json(
            """{"pullRequest":{"summary":1,"changes":"Adds retry.","verification":["Ran it."],"notes":2}}"""));

        // Assert
        text.ShouldBe(new RunPullRequestText(null, new RunPullRequestDescription(null, [], ["Ran it."], [], null)));
    }

    [Theory]
    [InlineData("""{"outcome":"done"}""")]
    [InlineData("""{"pullRequest":{}}""")]
    [InlineData("""{"pullRequest":{"body":"Adds retry."}}""")]
    public void WhenThereIsNoPullRequestText_ReturnsNull(string json)
    {
        // Act
        RunPullRequestText? text = RunPullRequestTextReader.Read(Json(json));

        // Assert
        text.ShouldBeNull();
    }
}
