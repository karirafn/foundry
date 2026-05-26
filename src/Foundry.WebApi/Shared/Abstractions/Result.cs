using System.Diagnostics;

namespace Foundry.WebApi.Shared.Abstractions;

public abstract class Result<T> where T : notnull
{
    private Result() { }

    public bool IsSuccess => this is Success;

    public bool IsFailure => this is Failure;

    public sealed class Success(T value) : Result<T>
    {
        public T Value { get; } = value;
    }

    public sealed class Failure(Error error) : Result<T>
    {
        public Error Error { get; } = error;
    }

    public TOut Match<TOut>(Func<T, TOut> onSuccess, Func<Error, TOut> onFailure) =>
        this switch
        {
            Success s => onSuccess(s.Value),
            Failure f => onFailure(f.Error),
            _ => throw new UnreachableException(),
        };

    public static Result<T> Ok(T value) => new Success(value);

    public static Result<T> Fail(Error error) => new Failure(error);

    public static implicit operator Result<T>(T value) => Ok(value);
}
