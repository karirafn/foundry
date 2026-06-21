namespace Foundry.Modules.Monitoring.Contracts;

public abstract record WorkerProvider
{
    private const string GitHubDiscriminator = "github";
    private const string GitLabDiscriminator = "gitlab";

    private WorkerProvider()
    {
    }

    public static WorkerProvider FromDiscriminator(string discriminator)
    {
        return discriminator switch
        {
            GitHubDiscriminator => new GitHub(),
            GitLabDiscriminator => new GitLab(),
            _ => throw new ArgumentException(
                $"Unknown provider discriminator: '{discriminator}'.",
                nameof(discriminator)),
        };
    }

    public sealed record GitHub : WorkerProvider;

    public sealed record GitLab : WorkerProvider;
}
