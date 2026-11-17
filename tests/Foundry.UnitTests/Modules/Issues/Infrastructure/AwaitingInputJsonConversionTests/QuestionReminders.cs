using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Issues.Infrastructure.Configurations;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Infrastructure.AwaitingInputJsonConversionTests;

public sealed class QuestionReminders
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 10, 0, 0, TimeSpan.Zero);

    private static QuestionReminder RoundTrip(QuestionReminder reminder) =>
        (QuestionReminder)AwaitingInputJsonConversion.QuestionReminderConverter.ConvertFromProvider(
            AwaitingInputJsonConversion.QuestionReminderConverter.ConvertToProvider(reminder))!;

    public static TheoryData<QuestionReminder> Reminders() =>
    [
        new QuestionReminder.Unsent(),
        new QuestionReminder.Sent(777, Now),
        new QuestionReminder.Failed("503", Now.AddMinutes(1), 1),
        new QuestionReminder.Failed("403", NextAttemptAt: null, 2),
    ];

    [Theory]
    [MemberData(nameof(Reminders))]
    public void EveryVariant_RoundTrips(QuestionReminder reminder)
    {
        // Act
        QuestionReminder read = RoundTrip(reminder);

        // Assert
        read.ShouldBe(reminder);
    }

    [Fact]
    public void AnUnsentReminder_IsWrittenAsItsKindAlone()
    {
        // Act
        string json = (string)AwaitingInputJsonConversion.QuestionReminderConverter.ConvertToProvider(new QuestionReminder.Unsent())!;

        // Assert
        json.ShouldContain("\"kind\":\"unsent\"");
    }
}
