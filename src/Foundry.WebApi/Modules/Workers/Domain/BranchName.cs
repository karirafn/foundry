namespace Foundry.WebApi.Modules.Workers.Domain;

public readonly record struct BranchName(string Value)
{
    public static BranchName From(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Branch name must not be empty or whitespace.", nameof(value));
        }

        return new BranchName(value);
    }
}
