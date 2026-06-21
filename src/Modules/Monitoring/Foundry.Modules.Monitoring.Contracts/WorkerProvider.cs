using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Contracts;

public abstract record WorkerProvider
{
    private const string GitHubDiscriminator = "github";
    private const string GitLabDiscriminator = "gitlab";

    private WorkerProvider()
    {
    }

    public static Result<WorkerProvider> FromDiscriminator(string discriminator)
    {
        return discriminator switch
        {
            GitHubDiscriminator => Result<WorkerProvider>.Ok(new GitHub()),
            GitLabDiscriminator => Result<WorkerProvider>.Ok(new GitLab()),
            _ => Result<WorkerProvider>.Fail(WorkerProviderErrors.UnknownDiscriminator(discriminator)),
        };
    }

    public sealed record GitHub : WorkerProvider;

    public sealed record GitLab : WorkerProvider;
}

public static class WorkerProviderErrors
{
    private const string UnknownDiscriminatorCode = "WorkerProvider.UnknownDiscriminator";

    public static Error UnknownDiscriminator(string discriminator) =>
        new(UnknownDiscriminatorCode, $"Unknown provider discriminator: '{discriminator}'.");
}
