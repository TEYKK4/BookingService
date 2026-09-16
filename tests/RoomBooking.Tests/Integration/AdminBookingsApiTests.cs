using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using RoomBooking.Contracts;
using RoomBooking.Models;
using Shouldly;

namespace RoomBooking.Tests.Integration;

public class AdminBookingsApiTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private ApiFactory _factory = null!;

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
            new AuthenticationHeaderValue("Bearer", TestJwt.ForUser(200, UserRole.Admin));
        return client;
    }

    /// <summary>A real registered user, so the admin list can show their login.</summary>
    private async Task<(HttpClient Client, string Login)> RegisteredUser()
    {
        var login = $"user{Guid.NewGuid():N}"[..20];
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/register", new CredentialsRequest(login, "secret123"));
        var token = (await response.Content.ReadFromJsonAsync<TokenResponse>())!.Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return (client, login);
    }

    private static int _dayOffset = 20;

    private static async Task<BookingResponse> Book(HttpClient client, int roomId)
    {
        var zone = BookingHours.ZoneOf("Europe/Warsaw");
        var day = BookingHours.TodayIn(zone).AddDays(Interlocked.Increment(ref _dayOffset));
        var slot = BookingHours.SlotsOn(day, zone).ElementAt(2);

        var response = await client.PostAsJsonAsync("/api/bookings", new CreateBookingRequest(roomId, slot));
        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        return (await response.Content.ReadFromJsonAsync<BookingResponse>())!;
    }

    [Fact]
    public async Task Admin_booking_endpoints_need_an_admin_token()
    {
        var (plain, _) = await RegisteredUser();

        (await _factory.CreateClient().GetAsync("/api/admin/bookings")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await plain.GetAsync("/api/admin/bookings")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_admin_sees_everyones_bookings_with_the_owners_login()
    {
        var (user, login) = await RegisteredUser();
        var booking = await Book(user, roomId: 1);

        var all = await Admin().GetFromJsonAsync<List<AdminBookingResponse>>("/api/admin/bookings");

        all.ShouldNotBeNull();
        var mine = all.Single(b => b.Id == booking.Id);
        mine.UserLogin.ShouldBe(login);
        mine.RoomName.ShouldBe("Focus");
    }

    [Fact]
    public async Task The_list_can_be_narrowed_to_one_room()
    {
        var (user, _) = await RegisteredUser();
        var inFocus = await Book(user, roomId: 1);
        var inHuddle = await Book(user, roomId: 2);

        var onlyHuddle = await Admin().GetFromJsonAsync<List<AdminBookingResponse>>("/api/admin/bookings?roomId=2");

        onlyHuddle.ShouldNotBeNull();
        onlyHuddle.ShouldContain(b => b.Id == inHuddle.Id);
        onlyHuddle.ShouldNotContain(b => b.Id == inFocus.Id);
    }

    [Fact]
    public async Task An_admin_can_cancel_anyones_booking()
    {
        var (user, _) = await RegisteredUser();
        var booking = await Book(user, roomId: 3);

        var cancelled = await Admin().DeleteAsync($"/api/admin/bookings/{booking.Id}");
        cancelled.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var stillMine = await user.GetFromJsonAsync<List<BookingResponse>>("/api/bookings/my");
        stillMine!.ShouldNotContain(b => b.Id == booking.Id);
    }

    [Fact]
    public async Task Cancelling_a_missing_booking_is_a_plain_404()
    {
        var response = await Admin().DeleteAsync("/api/admin/bookings/999999");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_unknown_scope_is_rejected()
    {
        var response = await Admin().GetAsync("/api/admin/bookings?scope=whenever");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
