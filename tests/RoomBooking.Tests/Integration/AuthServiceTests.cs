using AuthGrpcService;
using AuthGrpcService.Data;
using AuthGrpcService.Models;
using AuthGrpcService.Services;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Shouldly;

namespace RoomBooking.Tests.Integration;

public class AuthServiceTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private static readonly JwtSettings Jwt = new()
    {
        Issuer = "TestIssuer",
        Audience = "TestAudience",
        Key = "test-signing-key-that-is-long-enough-for-hmac-sha256",
        ExpiryMinutes = 60,
    };

    private readonly JwtTokenService _tokens = new(Options.Create(Jwt));

    private AppDbContext NewDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(postgres.ConnectionString)
        .Options);

    private AuthService NewService(AppDbContext db) =>
        new(NullLogger<AuthService>.Instance, db, _tokens);

    // ServerCallContext is never touched by these handlers.
    private static ServerCallContext NoContext => null!;

    private static string NewLogin() => $"user{Guid.NewGuid():N}"[..20];

    public async Task InitializeAsync()
    {
        await using var db = NewDb();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Register_creates_the_user_and_returns_a_token()
    {
        var login = NewLogin();
        await using var db = NewDb();

        var response = await NewService(db).Register(
            new Credentials { Login = login, Password = "secret123" }, NoContext);

        response.Token.ShouldNotBeNullOrWhiteSpace();
        (await db.Users.CountAsync(u => u.Login == login)).ShouldBe(1);
    }

    [Fact]
    public async Task Register_stores_a_hash_never_the_raw_password()
    {
        var login = NewLogin();
        await using var db = NewDb();

        await NewService(db).Register(new Credentials { Login = login, Password = "secret123" }, NoContext);

        var user = await db.Users.SingleAsync(u => u.Login == login);
        user.PasswordHash.ShouldNotBe("secret123");
        BCrypt.Net.BCrypt.Verify("secret123", user.PasswordHash).ShouldBeTrue();
    }

    [Fact]
    public async Task Register_with_a_taken_login_reports_AlreadyExists()
    {
        var login = NewLogin();
        await using var db = NewDb();
        var service = NewService(db);

        await service.Register(new Credentials { Login = login, Password = "secret123" }, NoContext);

        var exception = await Should.ThrowAsync<RpcException>(() =>
            service.Register(new Credentials { Login = login, Password = "another123" }, NoContext));

        exception.StatusCode.ShouldBe(StatusCode.AlreadyExists);
    }

    [Fact]
    public async Task Login_returns_a_token_for_the_right_password()
    {
        var login = NewLogin();
        await using var db = NewDb();
        var service = NewService(db);
        await service.Register(new Credentials { Login = login, Password = "secret123" }, NoContext);

        var response = await service.Login(new Credentials { Login = login, Password = "secret123" }, NoContext);

        response.Token.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Login_rejects_a_wrong_password()
    {
        var login = NewLogin();
        await using var db = NewDb();
        var service = NewService(db);
        await service.Register(new Credentials { Login = login, Password = "secret123" }, NoContext);

        var exception = await Should.ThrowAsync<RpcException>(() =>
            service.Login(new Credentials { Login = login, Password = "wrong-password" }, NoContext));

        exception.StatusCode.ShouldBe(StatusCode.Unauthenticated);
    }

    [Fact]
    public async Task Login_rejects_an_unknown_user_the_same_way_as_a_wrong_password()
    {
        await using var db = NewDb();

        var exception = await Should.ThrowAsync<RpcException>(() =>
            NewService(db).Login(new Credentials { Login = NewLogin(), Password = "secret123" }, NoContext));

        // Same status and message as a wrong password, so the response does not
        // reveal whether the login exists.
        exception.StatusCode.ShouldBe(StatusCode.Unauthenticated);
        exception.Status.Detail.ShouldBe("Invalid login or password");
    }

    [Fact]
    public async Task Concurrent_registrations_of_one_login_create_exactly_one_user()
    {
        var login = NewLogin();

        var attempts = Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var db = NewDb();
            try
            {
                await NewService(db).Register(new Credentials { Login = login, Password = "secret123" }, NoContext);
                return true;
            }
            catch (RpcException)
            {
                return false;
            }
        });

        var succeeded = (await Task.WhenAll(attempts)).Count(ok => ok);

        succeeded.ShouldBe(1);

        await using var verify = NewDb();
        (await verify.Users.CountAsync(u => u.Login == login)).ShouldBe(1);
    }

    [Fact]
    public async Task The_database_itself_refuses_a_duplicate_login()
    {
        // Goes around the service on purpose: this proves the unique index is
        // really there, not just that the handler checks first.
        var login = NewLogin();
        await using var db = NewDb();

        db.Users.Add(new User { Login = login, PasswordHash = "irrelevant" });
        await db.SaveChangesAsync();

        db.Users.Add(new User { Login = login, PasswordHash = "irrelevant too" });

        var exception = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());

        exception.InnerException.ShouldBeOfType<PostgresException>()
            .SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
    }
}
