using Foundry.Modules.Workers.Features;
using Foundry.Modules.Workers.Features.ContainerSpec;

using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.WorkerOptionsValidatorTests;

public sealed class Validate
{
    private readonly WorkerOptionsValidator _sut = new();

    [Fact]
    public void WhenAllValid_ReturnsSuccess()
    {
        // Arrange
        WorkerOptions options = new();

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void WhenCiCdDenyRulesContainsEmptyString_ReturnsFailure()
    {
        // Arrange
        WorkerOptions options = new()
        {
            Settings = new WorkerSettingsOptions { CiCdDenyRules = [string.Empty] },
        };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public void WhenCiCdDenyRulesContainsWhitespaceString_ReturnsFailure()
    {
        // Arrange
        WorkerOptions options = new()
        {
            Settings = new WorkerSettingsOptions { CiCdDenyRules = ["   "] },
        };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public void WhenCiCdDenyRulesIsEmpty_ReturnsSuccess()
    {
        // Arrange
        WorkerOptions options = new()
        {
            Settings = new WorkerSettingsOptions { CiCdDenyRules = [] },
        };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void WhenCiCdDenyRulesContainsValidEntries_ReturnsSuccess()
    {
        // Arrange
        WorkerOptions options = new()
        {
            Settings = new WorkerSettingsOptions { CiCdDenyRules = ["Edit(.github/workflows/**:*)"] },
        };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void WhenAdditionalDenyRulesContainsEmptyString_ReturnsFailure()
    {
        // Arrange
        WorkerOptions options = new()
        {
            Settings = new WorkerSettingsOptions { AdditionalDenyRules = [string.Empty] },
        };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public void WhenAdditionalDenyRulesContainsWhitespaceOnlyString_ReturnsFailure()
    {
        // Arrange
        WorkerOptions options = new()
        {
            Settings = new WorkerSettingsOptions { AdditionalDenyRules = ["   "] },
        };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public void WhenAdditionalDenyRulesContainsEmptyString_FailureMessageMentionsAdditionalDenyRules()
    {
        // Arrange
        WorkerOptions options = new()
        {
            Settings = new WorkerSettingsOptions { AdditionalDenyRules = [string.Empty] },
        };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        IEnumerable<string> failures = result.Failures.ShouldNotBeNull();
        failures.ShouldContain(f => f.Contains("AdditionalDenyRules"));
    }

    [Fact]
    public void WhenAdditionalDenyRulesContainsValidEntries_ReturnsSuccess()
    {
        // Arrange
        WorkerOptions options = new()
        {
            Settings = new WorkerSettingsOptions { AdditionalDenyRules = ["Bash(rm -rf:*)"] },
        };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void WhenHookEntryTypeIsNotCommand_ReturnsFailure()
    {
        // Arrange
        WorkerOptions options = new()
        {
            Settings = new WorkerSettingsOptions
            {
                Hooks = new Dictionary<string, List<HookGroup>>
                {
                    ["PreToolUse"] =
                    [
                        new HookGroup
                        {
                            Hooks = [new HookEntry { Type = "script", Command = "echo hi" }],
                        },
                    ],
                },
            },
        };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public void WhenHookEntryTypeIsNotCommand_FailureMessageMentionsCommand()
    {
        // Arrange
        WorkerOptions options = new()
        {
            Settings = new WorkerSettingsOptions
            {
                Hooks = new Dictionary<string, List<HookGroup>>
                {
                    ["PreToolUse"] =
                    [
                        new HookGroup
                        {
                            Hooks = [new HookEntry { Type = "script", Command = "echo hi" }],
                        },
                    ],
                },
            },
        };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        IEnumerable<string> failures = result.Failures.ShouldNotBeNull();
        failures.ShouldContain(f => f.Contains("\"command\""));
    }

    [Fact]
    public void WhenHookEntryCommandIsEmpty_ReturnsFailure()
    {
        // Arrange
        WorkerOptions options = new()
        {
            Settings = new WorkerSettingsOptions
            {
                Hooks = new Dictionary<string, List<HookGroup>>
                {
                    ["PreToolUse"] =
                    [
                        new HookGroup
                        {
                            Hooks = [new HookEntry { Type = "command", Command = string.Empty }],
                        },
                    ],
                },
            },
        };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public void WhenHookEntryCommandIsWhitespaceOnly_ReturnsFailure()
    {
        // Arrange
        WorkerOptions options = new()
        {
            Settings = new WorkerSettingsOptions
            {
                Hooks = new Dictionary<string, List<HookGroup>>
                {
                    ["PostToolUse"] =
                    [
                        new HookGroup
                        {
                            Hooks = [new HookEntry { Type = "command", Command = "   " }],
                        },
                    ],
                },
            },
        };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public void WhenHookEntryCommandIsEmpty_FailureMessageMentionsCommand()
    {
        // Arrange
        WorkerOptions options = new()
        {
            Settings = new WorkerSettingsOptions
            {
                Hooks = new Dictionary<string, List<HookGroup>>
                {
                    ["PreToolUse"] =
                    [
                        new HookGroup
                        {
                            Hooks = [new HookEntry { Type = "command", Command = string.Empty }],
                        },
                    ],
                },
            },
        };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        IEnumerable<string> failures = result.Failures.ShouldNotBeNull();
        failures.ShouldContain(f => f.Contains("Command"));
    }

    [Fact]
    public void WhenHooksAreValid_ReturnsSuccess()
    {
        // Arrange
        WorkerOptions options = new()
        {
            Settings = new WorkerSettingsOptions
            {
                Hooks = new Dictionary<string, List<HookGroup>>
                {
                    ["PreToolUse"] =
                    [
                        new HookGroup
                        {
                            Matcher = "Bash",
                            Hooks = [new HookEntry { Type = "command", Command = "echo pre-tool" }],
                        },
                    ],
                },
            },
        };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void WhenCiCdDenyRulesContainsEmptyString_FailureMessageMentionsCiCdDenyRules()
    {
        // Arrange
        WorkerOptions options = new()
        {
            Settings = new WorkerSettingsOptions { CiCdDenyRules = [string.Empty] },
        };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        IEnumerable<string> failures = result.Failures.ShouldNotBeNull();
        failures.ShouldContain(f => f.Contains("CiCdDenyRules"));
    }

    [Fact]
    public void WhenCiCdDenyRulesContainsMalformedEntry_ReturnsFailure()
    {
        // Arrange
        WorkerOptions options = new()
        {
            Settings = new WorkerSettingsOptions { CiCdDenyRules = ["not-a-valid-rule"] },
        };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public void WhenCiCdDenyRulesContainsValidEntry_ReturnsSuccess()
    {
        // Arrange
        WorkerOptions options = new()
        {
            Settings = new WorkerSettingsOptions { CiCdDenyRules = ["Edit(.github/workflows/**:*)"] },
        };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void WhenAdditionalDenyRulesContainsMalformedEntry_ReturnsFailure()
    {
        // Arrange
        WorkerOptions options = new()
        {
            Settings = new WorkerSettingsOptions { AdditionalDenyRules = ["not-a-valid-rule"] },
        };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public void WhenAdditionalDenyRulesContainsValidEntry_ReturnsSuccess()
    {
        // Arrange
        WorkerOptions options = new()
        {
            Settings = new WorkerSettingsOptions { AdditionalDenyRules = ["Bash(rm -rf:*)"] },
        };

        // Act
        ValidateOptionsResult result = _sut.Validate(null, options);

        // Assert
        result.Succeeded.ShouldBeTrue();
    }
}
