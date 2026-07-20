using System.Text.Json.Serialization;

using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Contracts;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(GitHub), typeDiscriminator: "github")]
[JsonDerivedType(typeof(GitLab), typeDiscriminator: "gitlab")]
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
