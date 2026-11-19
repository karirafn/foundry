using System.Text;

using Foundry.Modules.Monitoring.Infrastructure;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.JobLogTailReaderTests;

public sealed class ReadAsync
{
    private const int Tail = JobLogTailReader.JobLogTailBytes;

    private static Task<string> ActAsync(long length, bool startsMidLog = false) =>
        JobLogTailReader.ReadAsync(
            new StreamContent(new GeneratedLogStream(0, length)),
            startsMidLog,
            TestContext.Current.CancellationToken);

    [Fact]
    public void TheTailIs256KiB() => JobLogTailReader.JobLogTailBytes.ShouldBe(256 * 1024);

    [Fact]
    public async Task WhenTheBodyIsSmallerThanTheTail_ReturnsItWhole()
    {
        // Act
        string log = await ActAsync(Tail - 1);

        // Assert
        log.ShouldBe(GeneratedLogStream.TextOf(0, Tail - 1));
    }

    [Fact]
    public async Task WhenTheBodyIsExactlyTheTail_ReturnsItWhole()
    {
        // Act
        string log = await ActAsync(Tail);

        // Assert
        log.ShouldBe(GeneratedLogStream.TextOf(0, Tail));
    }

    [Fact]
    public async Task WhenTheBodyIsLargerThanTheTail_ReturnsOnlyItsLastTailBytes()
    {
        // Arrange
        long length = (3L * Tail) + 12345;

        // Act
        string log = await ActAsync(length);

        // Assert
        log.ShouldBe(GeneratedLogStream.TextOf(length - Tail, length));
    }

    [Fact]
    public async Task WhenTheBodyIsLargerThanTheResponseBufferCap_ReturnsItsLastTailBytes()
    {
        // Arrange
        long length = Foundry.Modules.Monitoring.MonitoringModule.MaxResponseContentBufferSize + 1024;

        // Act
        string log = await ActAsync(length);

        // Assert
        log.ShouldBe(GeneratedLogStream.TextOf(length - Tail, length));
    }

    [Fact]
    public async Task WhenTheBodyIsEmpty_ReturnsAnEmptyString()
    {
        // Act
        string log = await ActAsync(0);

        // Assert
        log.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenTheCutLandsInsideAMultiByteSequence_DropsThePartialLeadingSequence()
    {
        // Arrange: '€' is three bytes, and the tail size is not a multiple of three, so the cut lands mid-sequence.
        string body = new('€', Tail);
        ByteArrayContent content = new(Encoding.UTF8.GetBytes(body));

        // Act
        string log = await JobLogTailReader.ReadAsync(content, false, TestContext.Current.CancellationToken);

        // Assert
        log.ShouldBe(new string('€', Tail / 3));
    }

    [Fact]
    public async Task WhenTheBodyStartsMidLogInsideAMultiByteSequence_DropsThePartialLeadingSequence()
    {
        // Arrange: a range response that begins on the last continuation byte of '€'.
        byte[] euro = Encoding.UTF8.GetBytes("€");
        ByteArrayContent content = new([euro[2], .. Encoding.UTF8.GetBytes("ok")]);

        // Act
        string log = await JobLogTailReader.ReadAsync(content, true, TestContext.Current.CancellationToken);

        // Assert
        log.ShouldBe("ok");
    }

    [Fact]
    public async Task WhenTheWholeBodyStartsWithAByteOrderMark_DropsIt()
    {
        // Arrange
        ByteArrayContent content = new([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("log")]);

        // Act
        string log = await JobLogTailReader.ReadAsync(content, false, TestContext.Current.CancellationToken);

        // Assert
        log.ShouldBe("log");
    }
}
