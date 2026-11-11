using System.Diagnostics;

using Foundry.Shared;

namespace Foundry.Testing;

public static class ResultTestExtensions
{
    public static T ValueOrThrow<T>(this Result<T> result) where T : notnull =>
        result switch
        {
            Result<T>.Success success => success.Value,
            Result<T>.Failure failure => throw new InvalidOperationException(
                $"{failure.Error.Code}: {failure.Error.Message}"),
            _ => throw new UnreachableException(),
        };

    public static Error ErrorOrThrow<T>(this Result<T> result) where T : notnull =>
        result switch
        {
            Result<T>.Failure failure => failure.Error,
            Result<T>.Success => throw new InvalidOperationException("Expected a failure but the result succeeded."),
            _ => throw new UnreachableException(),
        };
}
