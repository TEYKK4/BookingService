using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using RoomBooking.Contracts;
using RoomBooking.Models;
using Shouldly;

namespace RoomBooking.Tests.Integration;

public class AdminUsersApiTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private ApiFactory _factory = null!;

    private const int AdminId = 300;

    public Task InitializeAsync()
    {
        _factory = new ApiFactory(postgres.ConnectionString);
        _ = _factory.Services;
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private HttpClient Admin()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwt.ForUser(AdminId, UserRole.Admin));
        return client;
    }

    private async Task<(HttpClient Client, int Id, string Login)> RegisteredUser()
    {
        var login = $"user{Guid.NewGuid():N}"[..20];
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/register", new CredentialsRequest(login, "secret123"));
        var token = (await response.Content.ReadFromJsonAsync<TokenResponse>())!.Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var me = await client.GetFromJsonAsync<MeResponse>("/api/auth/me");
        return (client, me!.Id, login);
    }

    [Fact]
    public async Task Admin_user_endpoints_need_an_admin_token()
    {
        var (plain, _, _) = await RegisteredUser();

        (await _factory.CreateClient().GetAsync("/api/admin/users")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await plain.GetAsync("/api/admin/users")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_list_shows_every_user_with_their_role()
    {
        var (_, id, login) = await RegisteredUser();

        var users = await Admin().GetFromJsonAsync<List<AdminUserResponse>>("/api/admin/users");

        users.ShouldNotBeNull();
        var me = users.Single(u => u.Id == id);
        me.Login.ShouldBe(login);
        me.Role.ShouldBe("User");
    }

    [Fact]
    public async Task An_admin_can_promote_a_user_and_the_new_role_shows_on_next_login()
    {
        var (_, id, login) = await RegisteredUser();

        var promoted = await Admin().PutAsJsonAsync($"/api/admin/users/{id}/role", new SetRoleRequest("admin"));
        promoted.StatusCode.ShouldBe(HttpStatusCode.OK);

        // The role travels in the token, so it appears after logging in again.
        var client = _factory.CreateClient();
        var login2 = await client.PostAsJsonAsync("/api/auth/login", new CredentialsRequest(login, "secret123"));
        var token = (await login2.Content.ReadFromJsonAsync<TokenResponse>())!.Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var me = await client.GetFromJsonAsync<MeResponse>("/api/auth/me");
        me!.Role.ShouldBe("Admin");

        // ...and the promoted user can now reach admin endpoints themselves.
        (await client.GetAsync("/api/admin/users")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_admin_cannot_remove_their_own_admin_role()
    {
        var response = await Admin().PutAsJsonAsync($"/api/admin/users/{AdminId}/role", new SetRoleRequest("User"));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task An_unknown_role_is_rejected()
    {
        var (_, id, _) = await RegisteredUser();

        var response = await Admin().PutAsJsonAsync($"/api/admin/users/{id}/role", new SetRoleRequest("Overlord"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Changing_the_role_of_a_missing_user_is_404()
    {
        var response = await Admin().PutAsJsonAsync("/api/admin/users/999999/role", new SetRoleRequest("Admin"));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
