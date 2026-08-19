using System.Text.Json.Serialization;

namespace Foundry.Modules.Monitoring.Contracts;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(GitHub), typeDiscriminator: "github")]
[JsonDerivedType(typeof(GitLab), typeDiscriminator: "gitlab")]
public abstract record WorkerProvider
{
    private WorkerProvider()
    {
    }

    public sealed record GitHub : WorkerProvider;

    public sealed record GitLab : WorkerProvider;
}
