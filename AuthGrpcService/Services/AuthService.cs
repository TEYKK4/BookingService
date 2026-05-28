using Grpc.Core;

namespace AuthGrpcService.Services;

public class AuthService(ILogger<AuthService> logger) : Auth.AuthBase
{
    public override Task<JwtToken> Login(Credentials request, ServerCallContext context)
    {
        return base.Login(request, context);
    }

    public override Task<JwtToken> Register(Credentials request, ServerCallContext context)
    {
        return base.Register(request, context);
    }
}