namespace Foundry.WebApi.Shared.Abstractions;

public abstract class Result
{
    private Result() { }

    public bool IsSuccess => this is Success;

    public bool IsFailure => this is Failure;

    public sealed class Success : Result;

    public sealed class Failure(Error error) : Result
    {
        public Error Error { get; } = error;
    }

    public TOut Match<TOut>(Func<TOut> onSuccess, Func<Error, TOut> onFailure) =>
        this switch
        {
            Success => onSuccess(),
            Failure f => onFailure(f.Error),
            _ => throw new InvalidOperationException("Unhandled Result subtype."),
        };

    public static Result Ok() => new Success();

    public static Result Fail(Error error) => new Failure(error);

    public static implicit operator Result(Error error) => Fail(error);
}
