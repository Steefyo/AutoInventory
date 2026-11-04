namespace AutoInventory.Api.Services;

public enum ResultKind { Ok, NotFound, Invalid, Conflict, Unavailable }

public sealed record ServiceResult<T>(
    ResultKind Kind,
    T? Value = default,
    IDictionary<string, string[]>? Errors = null,
    string? Message = null)
{
    public bool IsOk => Kind == ResultKind.Ok;

    public static ServiceResult<T> Ok(T value) => new(ResultKind.Ok, value);
    public static ServiceResult<T> NotFound(string message) => new(ResultKind.NotFound, Message: message);
    public static ServiceResult<T> Invalid(IDictionary<string, string[]> errors) => new(ResultKind.Invalid, Errors: errors);
    public static ServiceResult<T> Conflict(string message) => new(ResultKind.Conflict, Message: message);
    public static ServiceResult<T> Unavailable(string message) => new(ResultKind.Unavailable, Message: message);

    public ServiceResult<TOther> As<TOther>() => new(Kind, default, Errors, Message);
}

public readonly record struct Done;
