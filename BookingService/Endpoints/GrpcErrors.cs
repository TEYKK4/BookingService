using Grpc.Core;

namespace BookingService.Endpoints;

public static class GrpcErrors
{
    /// <summary>
    /// Turns a gRPC status from AuthService into the matching HTTP status.
    /// Without this every failure would surface as a 500.
    /// </summary>
    public static IResult ToHttpResult(RpcException exception) => exception.StatusCode switch
    {
        StatusCode.Unauthenticated => Results.Problem(exception.Status.Detail, statusCode: StatusCodes.Status401Unauthorized),
        StatusCode.AlreadyExists => Results.Problem(exception.Status.Detail, statusCode: StatusCodes.Status409Conflict),
        StatusCode.InvalidArgument => Results.Problem(exception.Status.Detail, statusCode: StatusCodes.Status400BadRequest),
        StatusCode.NotFound => Results.Problem(exception.Status.Detail, statusCode: StatusCodes.Status404NotFound),
        StatusCode.Unavailable => Results.Problem("Auth service is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable),
        _ => Results.Problem("Unexpected error talking to the auth service.", statusCode: StatusCodes.Status502BadGateway),
    };
}
