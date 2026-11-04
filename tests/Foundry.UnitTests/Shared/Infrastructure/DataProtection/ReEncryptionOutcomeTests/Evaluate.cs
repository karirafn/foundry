using Foundry.Shared.Infrastructure.DataProtection;

using Microsoft.AspNetCore.DataProtection;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Infrastructure.DataProtection.ReEncryptionOutcomeTests;

public sealed class Evaluate
{
    private const string Purpose = "Foundry.Test.Purpose";
    private const string Plaintext = "sécret-värde";

    private static readonly EphemeralDataProtectionProvider Root = new();

    private static readonly IDataProtector Current = Root.CreateProtector("Foundry").CreateProtector(Purpose);
    private static readonly IDataProtector Legacy = Root.CreateProtector("legacy").CreateProtector(Purpose);
    private static readonly IDataProtector Unrelated = Root.CreateProtector("unrelated").CreateProtector(Purpose);

    [Fact]
    public void ValueReadableUnderCurrent_ReturnsAlreadyCurrent()
    {
        // Arrange
        string stored = DataProtectedText.Protect(Current, Plaintext);

        // Act
        ReEncryptionOutcome outcome = ReEncryptionOutcome.Evaluate(stored, Current, Legacy);

        // Assert
        outcome.ShouldBeOfType<ReEncryptionOutcome.AlreadyCurrent>();
    }

    [Fact]
    public void ValueReadableOnlyUnderLegacy_ReturnsRewrittenValueReadableUnderCurrent()
    {
        // Arrange
        string stored = DataProtectedText.Protect(Legacy, Plaintext);

        // Act
        ReEncryptionOutcome outcome = ReEncryptionOutcome.Evaluate(stored, Current, Legacy);

        // Assert
        ReEncryptionOutcome.Rewritten rewritten = outcome.ShouldBeOfType<ReEncryptionOutcome.Rewritten>();
        DataProtectedText.Unprotect(Current, rewritten.NewStoredValue).ShouldBe(Plaintext);
    }

    [Fact]
    public void ValueReadableUnderNeither_ReturnsUndecryptable()
    {
        // Arrange
        string stored = DataProtectedText.Protect(Unrelated, Plaintext);

        // Act
        ReEncryptionOutcome outcome = ReEncryptionOutcome.Evaluate(stored, Current, Legacy);

        // Assert
        outcome.ShouldBeOfType<ReEncryptionOutcome.Undecryptable>();
    }

    [Fact]
    public void WithoutLegacyAndValueUnreadableUnderCurrent_ReturnsUndecryptable()
    {
        // Arrange
        string stored = DataProtectedText.Protect(Legacy, Plaintext);

        // Act
        ReEncryptionOutcome outcome = ReEncryptionOutcome.Evaluate(stored, Current, legacy: null);

        // Assert
        outcome.ShouldBeOfType<ReEncryptionOutcome.Undecryptable>();
    }

    [Fact]
    public void NonBase64Value_ReturnsUndecryptable()
    {
        // Arrange
        const string stored = "not base64 !!!";

        // Act
        ReEncryptionOutcome outcome = ReEncryptionOutcome.Evaluate(stored, Current, Legacy);

        // Assert
        outcome.ShouldBeOfType<ReEncryptionOutcome.Undecryptable>();
    }

    [Fact]
    public void ValueWhoseRewriteDoesNotRoundTrip_ReturnsUndecryptable()
    {
        // Arrange
        string stored = DataProtectedText.Protect(Legacy, Plaintext);
        IDataProtector corruptingCurrent = new CorruptingProtector(Current);

        // Act
        ReEncryptionOutcome outcome = ReEncryptionOutcome.Evaluate(stored, corruptingCurrent, Legacy);

        // Assert
        outcome.ShouldBeOfType<ReEncryptionOutcome.Undecryptable>();
    }

    private sealed class CorruptingProtector(IDataProtector inner) : IDataProtector
    {
        public IDataProtector CreateProtector(string purpose) => new CorruptingProtector(inner.CreateProtector(purpose));

        public byte[] Protect(byte[] plaintext) => inner.Protect([.. plaintext, (byte)'x']);

        public byte[] Unprotect(byte[] protectedData) => inner.Unprotect(protectedData);
    }
}
