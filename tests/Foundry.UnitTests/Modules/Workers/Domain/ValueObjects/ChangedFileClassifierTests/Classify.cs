using Foundry.Modules.Workers.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.ValueObjects.ChangedFileClassifierTests;

public sealed class Classify
{
    [Theory]
    [InlineData("test/foo.cs")]
    [InlineData("src/tests/foo.cs")]
    [InlineData("web/__tests__/app.js")]
    [InlineData("spec/models/user_rb.rb")]
    [InlineData("tests/Foundry.UnitTests/Foo.cs")]
    [InlineData("src/Foundry.Tests/Foo.cs")]
    [InlineData("src/Foundry.UnitTests/Foo.cs")]
    [InlineData("src/Foundry.IntegrationTests/Foo.cs")]
    [InlineData("src/app/user.spec.ts")]
    [InlineData("src/app/user.test.js")]
    [InlineData("pkg/server_test.go")]
    [InlineData("app/test_models.py")]
    [InlineData("workers/publisher/publisher.bats")]
    [InlineData("tests/README.md")]
    public void WhenAPathMatchesATestRule_ReturnsTest(string path)
    {
        // Act
        ChangedFileClass changedFileClass = ChangedFileClassifier.Classify(path);

        // Assert
        changedFileClass.ShouldBe(ChangedFileClass.Test);
    }

    [Theory]
    [InlineData("docs/adr/0113.md")]
    [InlineData("docs/diagram.drawio")]
    [InlineData("README.md")]
    [InlineData("notes.txt")]
    [InlineData("assets/logo.png")]
    [InlineData("assets/photo.jpg")]
    [InlineData("assets/spinner.gif")]
    [InlineData("assets/icon.svg")]
    public void WhenAPathIsDocumentationOrAnAsset_ReturnsOther(string path)
    {
        // Act
        ChangedFileClass changedFileClass = ChangedFileClassifier.Classify(path);

        // Assert
        changedFileClass.ShouldBe(ChangedFileClass.Other);
    }

    [Theory]
    [InlineData("src/Foundry/Program.cs")]
    [InlineData("src/testing/helpers.cs")]
    [InlineData("src/latest/thing.go")]
    [InlineData("src/contest_entry.py")]
    [InlineData("src/app/test")]
    [InlineData("src/docs/generator.cs")]
    [InlineData("Dockerfile")]
    public void WhenNoRuleMatches_ReturnsProduction(string path)
    {
        // Act
        ChangedFileClass changedFileClass = ChangedFileClassifier.Classify(path);

        // Assert
        changedFileClass.ShouldBe(ChangedFileClass.Production);
    }

    [Fact]
    public void WhenGitQuotedThePath_ClassifiesTheUnquotedPath()
    {
        // Act
        ChangedFileClass changedFileClass = ChangedFileClassifier.Classify("\"tests/caf\\303\\251.cs\"");

        // Assert
        changedFileClass.ShouldBe(ChangedFileClass.Test);
    }
}
