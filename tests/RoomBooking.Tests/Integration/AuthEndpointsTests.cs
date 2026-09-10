using System.Net;
using System.Net.Http.Json;
using AuthGrpcService;
using BookingService.Contracts;
using Grpc.Core;
using NSubstitute;
using Shouldly;

namespace RoomBooking.Tests.Integration;

/// <summary>
/// Covers the seam between the two services: BookingService forwards
/// login/register to AuthService over gRPC and turns gRPC statuses into HTTP ones.
/// AuthService itself is replaced by a stand-in, so only the mapping is tested.
/// </summary>
public class AuthEndpointsTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static AsyncUnaryCall<T> Completed<T>(T response) => new(
        Task.FromResult(response),
        Task.FromResult(new Metadata()),
        () => Status.DefaultSuccess,
        () => new Metadata(),
        () => { });

    private HttpClient ClientWith(Auth.AuthClient authClient) =>
        new BookingApiFactory(postgres.ConnectionString, authClient).CreateClient();

    private static Auth.AuthClient StandInAuth() => Substitute.For<Auth.AuthClient>();

    [Fact]
    public async Task Register_passes_the_token_from_the_auth_service_through()
    {
        var auth = StandInAuth();
        auth.RegisterAsync(Arg.Any<Credentials>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Completed(new JwtToken { Token = "token-from-auth" }));

        var response = await ClientWith(auth)
            .PostAsJsonAsync("/api/auth/register", new CredentialsRequest("bob", "secret123"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<TokenResponse>())!.Token.ShouldBe("token-from-auth");
    }

    [Fact]
    public async Task A_taken_login_comes_back_as_409_not_500()
    {
        var auth = StandInAuth();
        auth.RegisterAsync(Arg.Any<Credentials>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new RpcException(new Status(StatusCode.AlreadyExists, "taken")));

        var response = await ClientWith(auth)
            .PostAsJsonAsync("/api/auth/register", new CredentialsRequest("bob", "secret123"));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Bad_credentials_come_back_as_401()
    {
        var auth = StandInAuth();
        auth.LoginAsync(Arg.Any<Credentials>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new RpcException(new Status(StatusCode.Unauthenticated, "nope")));

        var response = await ClientWith(auth)
            .PostAsJsonAsync("/api/auth/login", new CredentialsRequest("bob", "wrong-password"));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Validation_failures_from_the_auth_service_come_back_as_400()
    {
        var auth = StandInAuth();
        auth.RegisterAsync(Arg.Any<Credentials>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new RpcException(new Status(StatusCode.InvalidArgument, "Login is required")));

        var response = await ClientWith(auth)
            .PostAsJsonAsync("/api/auth/register", new CredentialsRequest("", "secret123"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_unreachable_auth_service_comes_back_as_503()
    {
        var auth = StandInAuth();
        auth.LoginAsync(Arg.Any<Credentials>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new RpcException(new Status(StatusCode.Unavailable, "down")));

        var response = await ClientWith(auth)
            .PostAsJsonAsync("/api/auth/login", new CredentialsRequest("bob", "secret123"));

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }
}
