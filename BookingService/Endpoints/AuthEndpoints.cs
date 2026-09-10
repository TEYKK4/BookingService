using AuthGrpcService;
using BookingService.Contracts;
using Grpc.Core;

namespace BookingService.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/register", (CredentialsRequest request, Auth.AuthClient auth, CancellationToken ct) =>
            IssueToken(() => auth.RegisterAsync(ToCredentials(request), cancellationToken: ct).ResponseAsync));

        group.MapPost("/login", (CredentialsRequest request, Auth.AuthClient auth, CancellationToken ct) =>
            IssueToken(() => auth.LoginAsync(ToCredentials(request), cancellationToken: ct).ResponseAsync));
    }

    private static Credentials ToCredentials(CredentialsRequest request) =>
        new() { Login = request.Login ?? string.Empty, Password = request.Password ?? string.Empty };

    private static async Task<IResult> IssueToken(Func<Task<JwtToken>> call)
    {
        try
        {
            var token = await call();
            return Results.Ok(new TokenResponse(token.Token));
        }
        catch (RpcException exception)
        {
            return GrpcErrors.ToHttpResult(exception);
        }
    }
}
