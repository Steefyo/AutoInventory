using AutoInventory.Api.Services;

namespace AutoInventory.Api.Endpoints;

public static class ResultHttpExtensions
{
    public static IResult ToHttp<T>(this ServiceResult<T> r, Func<T, IResult> onOk) => r.Kind switch
    {
        ResultKind.Ok => onOk(r.Value!),
        ResultKind.Invalid => Results.ValidationProblem(r.Errors!),
        ResultKind.NotFound => Results.Problem(r.Message, statusCode: StatusCodes.Status404NotFound),
        ResultKind.Conflict => Results.Problem(r.Message, statusCode: StatusCodes.Status409Conflict),
        ResultKind.Unavailable => Results.Problem(r.Message, statusCode: StatusCodes.Status502BadGateway),
        _ => throw new InvalidOperationException($"Unknown result kind {r.Kind}"),
    };
}
