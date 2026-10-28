using Foundry.Shared;

namespace Foundry.Testing;

public sealed class FailingCheckBuilder
{
    private string _name = "build";
    private string _conclusion = "failure";
    private string? _detailsUrl;

    public FailingCheckBuilder WithName(string name)
    {
        _name = name;
        return this;
    }

    public FailingCheckBuilder WithConclusion(string conclusion)
    {
        _conclusion = conclusion;
        return this;
    }

    public FailingCheckBuilder WithDetailsUrl(string? detailsUrl)
    {
        _detailsUrl = detailsUrl;
        return this;
    }

    public FailingCheck Build() => new(Name: _name, Conclusion: _conclusion, DetailsUrl: _detailsUrl);
}
