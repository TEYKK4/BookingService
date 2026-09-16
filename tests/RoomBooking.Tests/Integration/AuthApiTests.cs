using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RoomBooking.Contracts;
using RoomBooking.Data;
using RoomBooking.Models;
using Shouldly;

namespace RoomBooking.Tests.Integration;

public class AuthApiTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private ApiFactory _factory = null!;

    public Task InitializeAsync()
    {
        _factory = new ApiFactory(postgres.ConnectionString);

        // Touching Services builds the host, which runs migrations, so tests that
        // write straight to the database do not depend on another test going first.
        _ = _factory.Services;

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private HttpClient Client() => _factory.CreateClient();

    private AppDbContext NewDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(postgres.ConnectionString)
        .Options);

    private static string NewLogin() => $"user{Guid.NewGuid():N}"[..20];

    private static Task<HttpResponseMessage> Register(HttpClient client, string login, string password) =>
        client.PostAsJsonAsync("/api/auth/register", new CredentialsRequest(login, password));

    private static Task<HttpResponseMessage> Login(HttpClient client, string login, string password) =>
        client.PostAsJsonAsync("/api/auth/login", new CredentialsRequest(login, password));

    private static async Task<string> Detail(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        return problem!["detail"].ToString()!;
    }

    [Fact]
    public async Task Register_creates_the_user_and_returns_a_token()
    {
        var login = NewLogin();

        var response = await Register(Client(), login, "secret123");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<TokenResponse>())!.Token.ShouldNotBeNullOrWhiteSpace();

        await using var db = NewDb();
        (await db.Users.CountAsync(u => u.Login == login)).ShouldBe(1);
    }

    [Fact]
    public async Task Register_stores_a_hash_never_the_raw_password()
    {
        var login = NewLogin();

        await Register(Client(), login, "secret123");

        await using var db = NewDb();
        var user = await db.Users.SingleAsync(u => u.Login == login);
        user.PasswordHash.ShouldNotBe("secret123");
        BCrypt.Net.BCrypt.Verify("secret123", user.PasswordHash).ShouldBeTrue();
    }

    [Theory]
    [InlineData("ab", "secret123")]      // login too short
    public async Task Register_rejects_malformed_credentials_before_touching_the_database(
        string login, string password)
    {
        var response = await Register(Client(), login, password);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        await using var db = NewDb();
        (await db.Users.CountAsync(u => u.Login == login)).ShouldBe(0);
    }

    [Fact]
    public async Task Register_with_a_taken_login_is_409()
    {
        var login = NewLogin();
        await Register(Client(), login, "secret123");

        var second = await Register(Client(), login, "another123");

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Login_returns_a_token_for_the_right_password()
    {
        var login = NewLogin();
        await Register(Client(), login, "secret123");

        var response = await Login(Client(), login, "secret123");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<TokenResponse>())!.Token.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Login_rejects_a_wrong_password()
    {
        var login = NewLogin();
        await Register(Client(), login, "secret123");

        var response = await Login(Client(), login, "wrong-password");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_rejects_an_unknown_user_the_same_way_as_a_wrong_password()
    {
        var login = NewLogin();
        await Register(Client(), login, "secret123");

        var wrongPassword = await Login(Client(), login, "wrong-password");
        var unknownUser = await Login(Client(), NewLogin(), "secret123");

        // Same status and same message, so the response does not reveal which
        // half was wrong - that would let an attacker enumerate logins.
        unknownUser.StatusCode.ShouldBe(wrongPassword.StatusCode);
        (await Detail(unknownUser)).ShouldBe(await Detail(wrongPassword));
    }

    [Fact]
    public async Task Login_with_malformed_credentials_is_just_401()
    {
        // No 400 here on purpose: login must not reveal the validation rules.
        var response = await Login(Client(), "x", "");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_token_from_register_opens_a_protected_endpoint()
    {
        var registered = await Register(Client(), NewLogin(), "secret123");
        var token = (await registered.Content.ReadFromJsonAsync<TokenResponse>())!.Token;

        var client = Client();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/bookings/my");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Concurrent_registrations_of_one_login_create_exactly_one_user()
    {
        var login = NewLogin();

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => Register(Client(), login, "secret123")));

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBe(7);

        await using var db = NewDb();
        (await db.Users.CountAsync(u => u.Login == login)).ShouldBe(1);
    }

    [Fact]
    public async Task The_database_itself_refuses_a_duplicate_login()
    {
        // Goes around the API on purpose: this proves the unique index is really
        // there, not just that the handler checks first.
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
