namespace Foundry.Modules.Workers.Domain.ValueObjects;

/// <summary>One numbered acceptance criterion of an issue, which a review answers by its <see cref="Id"/>.</summary>
public sealed record AcceptanceCriterion
{
    public const string IdPrefix = "AC";

    public AcceptanceCriterion(int number, string text)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(number, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        Number = number;
        Text = text;
    }

    /// <summary>The criterion's 1-based position in its list.</summary>
    public int Number { get; }

    public string Text { get; }

    /// <summary>The id a verdict cites: <c>AC&lt;n&gt;</c>.</summary>
    public string Id => IdPrefix + Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
