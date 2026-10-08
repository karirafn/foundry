using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Features.Repositories;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Repositories.CreateRepositoryValidatorTests;

public sealed class Validate
{
    [Fact]
    public void WhenSlugIsEmpty_ReturnsFailure()
    {
        // Arrange
        CreateRepository.Validator sut = new();
        CreateRepository.Command command = new(Guid.NewGuid(), string.Empty, PollIntervalSeconds: null);

        // Act
        Result result = sut.Validate(command);

        // Assert
        result.IsFailure.ShouldBeTrue();
        ((Result.Failure)result).Error.Code.ShouldBe(CreateRepository.Validator.SlugEmptyCode);
    }

    [Fact]
    public void WhenSlugIsWhitespace_ReturnsFailure()
    {
        // Arrange
        CreateRepository.Validator sut = new();
        CreateRepository.Command command = new(Guid.NewGuid(), "   ", PollIntervalSeconds: null);

        // Act
        Result result = sut.Validate(command);

        // Assert
        result.IsFailure.ShouldBeTrue();
        ((Result.Failure)result).Error.Code.ShouldBe(CreateRepository.Validator.SlugEmptyCode);
    }

    [Fact]
    public void WhenSlugIsValid_ReturnsSuccess()
    {
        // Arrange
        CreateRepository.Validator sut = new();
        CreateRepository.Command command = new(Guid.NewGuid(), "owner/repo", PollIntervalSeconds: null);

        // Act
        Result result = sut.Validate(command);

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void WhenPollIntervalIsZero_ReturnsSuccess()
    {
        // Arrange — poll interval validation lives on the aggregate (MonitoredRepository.Create), not the validator
        CreateRepository.Validator sut = new();
        CreateRepository.Command command = new(Guid.NewGuid(), "owner/repo", PollIntervalSeconds: 0);

        // Act
        Result result = sut.Validate(command);

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void WhenPollIntervalExceedsMaximum_ReturnsSuccess()
    {
        // Arrange — poll interval validation lives on the aggregate (MonitoredRepository.Create), not the validator
        CreateRepository.Validator sut = new();
        CreateRepository.Command command = new(
            Guid.NewGuid(),
            "owner/repo",
            PollIntervalSeconds: MonitoredRepository.MaxPollIntervalSeconds + 1);

        // Act
        Result result = sut.Validate(command);

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }
}
