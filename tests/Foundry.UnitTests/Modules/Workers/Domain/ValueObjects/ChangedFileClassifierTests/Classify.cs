using Foundry.Modules.Workers.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.ValueObjects.ChangedFileClassifierTests;

public sealed class Classify
{
    [Theory]
    [InlineData("test/helpers.sh")]
    [InlineData("src/tests/util.py")]
    [InlineData("web/__tests__/app.js")]
    [InlineData("spec/models/user_model.rb")]
    [InlineData("tests/Foundry.Tests/Thing.cs")]
    [InlineData("src/Foundry.UnitTests/ThingTests.cs")]
    [InlineData("src/Foundry.IntegrationTests/ApiTests.cs")]
    [InlineData("src/app/app.spec.ts")]
    [InlineData("src/app/app.test.tsx")]
    [InlineData("pkg/server/server_test.go")]
    [InlineData("tools/test_parser.py")]
    [InlineData("workers/publisher/publisher.bats")]
    [InlineData("docs/tests/howto.md")]
    [InlineData("\"src/tests/a b.cs\"")]
    public void WhenTheDirectoriesOrNameMarkATest_ClassesItAsTest(string path)
    {
        // Act / Assert
        ChangedFileClassifier.Classify(path).ShouldBe(ChangedFileClass.Test);
    }

    [Theory]
    [InlineData("docs/adr/0113-review.md")]
    [InlineData("docs/diagram.drawio")]
    [InlineData("README.md")]
    [InlineData("notes/todo.txt")]
    [InlineData("assets/logo.png")]
    [InlineData("assets/photo.jpg")]
    [InlineData("assets/spinner.gif")]
    [InlineData("src/foundry-web/public/icon.svg")]
    public void WhenThePathIsDocumentationOrAnImage_ClassesItAsOther(string path)
    {
        // Act / Assert
        ChangedFileClassifier.Classify(path).ShouldBe(ChangedFileClass.Other);
    }

    [Theory]
    [InlineData("src/Modules/Workers/WorkerRun.cs")]
    [InlineData("workers/entrypoint.sh")]
    [InlineData("src/testing/helpers.cs")]
    [InlineData("src/contest.py")]
    [InlineData("src/latest.go")]
    [InlineData("src/app/app.spec")]
    [InlineData("src/docs/generator.cs")]
    [InlineData("Dockerfile")]
    public void WhenNoTestOrOtherRuleMatches_ClassesItAsProduction(string path)
    {
        // Act / Assert
        ChangedFileClassifier.Classify(path).ShouldBe(ChangedFileClass.Production);
    }
}
