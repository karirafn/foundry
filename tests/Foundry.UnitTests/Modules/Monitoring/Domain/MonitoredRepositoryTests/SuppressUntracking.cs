using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.MonitoredRepositoryTests;

public sealed class SuppressUntracking
{
    private static readonly DateTimeOffset FirstSuppressedAt = new(2026, 5, 27, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset LaterTime = new(2026, 5, 28, 14, 0, 0, TimeSpan.Zero);

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("octocat/hello-world").ValueOrThrow();

    [Fact]
    public void WhenCalledOnUnsuppressedRepository_SetsUntrackSuppressedSince()
    {
        // Arrange
        MonitoredRepository repository = MonitoredRepository.Create(ValidSlug, "github.com", null);

        // Act
        repository.SuppressUntracking(FirstSuppressedAt);

        // Assert
        repository.UntrackSuppressedSince.ShouldBe(FirstSuppressedAt);
    }

    [Fact]
    public void WhenCalledOnUnsuppressedRepository_ReturnsTrue()
    {
        // Arrange
        MonitoredRepository repository = MonitoredRepository.Create(ValidSlug, "github.com", null);

        // Act
        bool transitioned = repository.SuppressUntracking(FirstSuppressedAt);

        // Assert
        transitioned.ShouldBeTrue();
    }

    [Fact]
    public void WhenCalledAgainWithLaterTime_DoesNotChangeOriginalTimestamp()
    {
        // Arrange
        MonitoredRepository repository = MonitoredRepository.Create(ValidSlug, "github.com", null);
        repository.SuppressUntracking(FirstSuppressedAt);

        // Act
        repository.SuppressUntracking(LaterTime);

        // Assert
        repository.UntrackSuppressedSince.ShouldBe(FirstSuppressedAt);
    }

    [Fact]
    public void WhenCalledAgainWhenAlreadySuppressed_ReturnsFalse()
    {
        // Arrange
        MonitoredRepository repository = MonitoredRepository.Create(ValidSlug, "github.com", null);
        repository.SuppressUntracking(FirstSuppressedAt);

        // Act
        bool transitioned = repository.SuppressUntracking(LaterTime);

        // Assert
        transitioned.ShouldBeFalse();
    }

    [Fact]
    public void WhenClearUntrackSuppressionCalled_ResetsToNull()
    {
        // Arrange
        MonitoredRepository repository = MonitoredRepository.Create(ValidSlug, "github.com", null);
        repository.SuppressUntracking(FirstSuppressedAt);

        // Act
        repository.ClearUntrackSuppression();

        // Assert
        repository.UntrackSuppressedSince.ShouldBeNull();
    }

    [Fact]
    public void WhenClearUntrackSuppressionCalledOnUnsuppressedRepository_RemainsNull()
    {
        // Arrange
        MonitoredRepository repository = MonitoredRepository.Create(ValidSlug, "github.com", null);

        // Act
        repository.ClearUntrackSuppression();

        // Assert
        repository.UntrackSuppressedSince.ShouldBeNull();
    }

    [Fact]
    public void WhenClearedAndSuppressedAgain_SetsNewTimestamp()
    {
        // Arrange
        MonitoredRepository repository = MonitoredRepository.Create(ValidSlug, "github.com", null);
        repository.SuppressUntracking(FirstSuppressedAt);
        repository.ClearUntrackSuppression();

        // Act
        bool transitioned = repository.SuppressUntracking(LaterTime);

        // Assert
        transitioned.ShouldBeTrue();
        repository.UntrackSuppressedSince.ShouldBe(LaterTime);
    }
}
