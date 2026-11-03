using Foundry.Shared.Infrastructure.Docker;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Infrastructure.Docker.DockerLogLineTests;

public sealed class Split
{
    [Theory]
    [InlineData("2026-11-03T08:36:14.124595895Z ", """{"type":"rate_limit_event"}""")]
    [InlineData("2026-06-29T21:24:05Z ", """{"type":"result"}""")]
    [InlineData("2026-06-29T21:24:05.123+02:00 ", """{"type":"result"}""")]
    [InlineData("2026-06-29T21:24:05-05:00 ", "plain stderr text")]
    [InlineData("2026-06-29T21:24:05.1Z ", "")]
    public void WhenLineHasDockerTimestamp_SplitsPrefixFromBody(string prefix, string body)
    {
        // Act
        (string actualPrefix, string actualBody) = DockerLogLine.Split(prefix + body);

        // Assert
        actualPrefix.ShouldBe(prefix);
        actualBody.ShouldBe(body);
    }

    [Theory]
    [InlineData("""{"type":"result"}""")]
    [InlineData("Warning: no stdin data received in 3s, proceeding without it.")]
    [InlineData("")]
    [InlineData("2026-06-29T21:24:05Z{\"no\":\"space\"}")]
    [InlineData("2026-06-29 21:24:05Z {\"no\":\"T\"}")]
    [InlineData(" 2026-06-29T21:24:05Z {\"leading\":\"space\"}")]
    public void WhenLineHasNoDockerTimestamp_ReturnsLineUnchanged(string line)
    {
        // Act
        (string prefix, string body) = DockerLogLine.Split(line);

        // Assert
        prefix.ShouldBeEmpty();
        body.ShouldBe(line);
    }

    [Fact]
    public void StripTimestamp_ReturnsBodyOnly()
    {
        // Act
        string body = DockerLogLine.StripTimestamp("""2026-11-03T08:36:14.124595895Z {"type":"assistant"}""");

        // Assert
        body.ShouldBe("""{"type":"assistant"}""");
    }

    [Fact]
    public void ParseTimestamp_WhenPrefixIsValid_ReturnsInstant()
    {
        // Act
        DateTimeOffset? parsed = DockerLogLine.ParseTimestamp("2026-06-29T21:24:05.123456789Z ");

        // Assert
        DateTimeOffset value = parsed.ShouldNotBeNull();
        value.Offset.ShouldBe(TimeSpan.Zero);
        value.ShouldBeInRange(
            new DateTimeOffset(2026, 6, 29, 21, 24, 5, 123, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 29, 21, 24, 5, 124, TimeSpan.Zero));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-timestamp ")]
    public void ParseTimestamp_WhenPrefixIsEmptyOrInvalid_ReturnsNull(string prefix)
    {
        // Act
        DateTimeOffset? parsed = DockerLogLine.ParseTimestamp(prefix);

        // Assert
        parsed.ShouldBeNull();
    }
}
