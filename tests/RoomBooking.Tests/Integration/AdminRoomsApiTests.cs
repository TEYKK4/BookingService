using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using RoomBooking.Contracts;
using RoomBooking.Models;
using Shouldly;

namespace RoomBooking.Tests.Integration;

public class AdminRoomsApiTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private ApiFactory _factory = null!;

    private const int AdminId = 100;
    private const int PlainId = 101;

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

    private HttpClient Anonymous() => _factory.CreateClient();

    private HttpClient As(int userId, UserRole role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwt.ForUser(userId, role));
        return client;
    }

    private HttpClient Admin() => As(AdminId, UserRole.Admin);
    private HttpClient Plain() => As(PlainId, UserRole.User);

    private static SaveRoomRequest NewRoom(string? zone = "Europe/Warsaw") =>
        new($"Room {Guid.NewGuid():N}"[..20], 4, zone!);

    private static async Task<AdminRoomResponse> Create(HttpClient admin, SaveRoomRequest request)
    {
        var response = await admin.PostAsJsonAsync("/api/admin/rooms", request);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<AdminRoomResponse>())!;
    }

    private static async Task<string> Detail(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        return problem!["detail"].ToString()!;
    }

    [Fact]
    public async Task Admin_endpoints_need_an_admin_token()
    {
        (await Anonymous().GetAsync("/api/admin/rooms")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Plain().GetAsync("/api/admin/rooms")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Plain().PostAsJsonAsync("/api/admin/rooms", NewRoom())).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_admin_can_create_a_room_and_users_see_it()
    {
        var request = NewRoom();

        var created = await Create(Admin(), request);

        created.Name.ShouldBe(request.Name);
        created.IsActive.ShouldBeTrue();

        var visible = await Anonymous().GetFromJsonAsync<List<RoomResponse>>("/api/rooms");
        visible.ShouldNotBeNull();
        visible.ShouldContain(r => r.Id == created.Id);
    }

    [Fact]
    public async Task An_unknown_time_zone_is_rejected_with_a_message_naming_it()
    {
        var response = await Admin().PostAsJsonAsync("/api/admin/rooms", NewRoom("Mars/Base"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Detail(response)).ShouldContain("Mars/Base");
    }

    [Theory]
    [InlineData("", 4)]
    [InlineData("Ok", 0)]
    [InlineData("Ok", 1001)]
    public async Task Name_and_capacity_are_validated(string name, int capacity)
    {
        var response = await Admin().PostAsJsonAsync("/api/admin/rooms",
            new SaveRoomRequest(name, capacity, "Europe/Warsaw"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Deactivating_hides_a_room_from_users_but_not_from_admins()
    {
        var admin = Admin();
        var room = await Create(admin, NewRoom());

        (await admin.PostAsync($"/api/admin/rooms/{room.Id}/deactivate", null))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var visible = await Anonymous().GetFromJsonAsync<List<RoomResponse>>("/api/rooms");
        visible!.ShouldNotContain(r => r.Id == room.Id);

        var all = await admin.GetFromJsonAsync<List<AdminRoomResponse>>("/api/admin/rooms");
        all!.Single(r => r.Id == room.Id).IsActive.ShouldBeFalse();

        var zone = BookingHours.ZoneOf(room.TimeZoneId);
        var slot = BookingHours.SlotsOn(BookingHours.TodayIn(zone).AddDays(7), zone).First();
        (await Plain().PostAsJsonAsync("/api/bookings", new CreateBookingRequest(room.Id, slot)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await admin.PostAsync($"/api/admin/rooms/{room.Id}/activate", null))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        visible = await Anonymous().GetFromJsonAsync<List<RoomResponse>>("/api/rooms");
        visible!.ShouldContain(r => r.Id == room.Id);
    }

    [Fact]
    public async Task Name_and_capacity_can_be_changed()
    {
        var admin = Admin();
        var room = await Create(admin, NewRoom());

        var response = await admin.PutAsJsonAsync($"/api/admin/rooms/{room.Id}",
            new SaveRoomRequest("Renamed", 12, room.TimeZoneId));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var visible = await Anonymous().GetFromJsonAsync<List<RoomResponse>>("/api/rooms");
        var updated = visible!.Single(r => r.Id == room.Id);
        updated.Name.ShouldBe("Renamed");
        updated.Capacity.ShouldBe(12);
    }

    [Fact]
    public async Task The_time_zone_cannot_change_while_bookings_are_upcoming()
    {
        var admin = Admin();
        var room = await Create(admin, NewRoom("Europe/Warsaw"));

        var warsaw = BookingHours.ZoneOf("Europe/Warsaw");
        var slot = BookingHours.SlotsOn(BookingHours.TodayIn(warsaw).AddDays(8), warsaw).ElementAt(3);
        (await Plain().PostAsJsonAsync("/api/bookings", new CreateBookingRequest(room.Id, slot)))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var moved = await admin.PutAsJsonAsync($"/api/admin/rooms/{room.Id}",
            new SaveRoomRequest(room.Name, room.Capacity, "Europe/London"));
        moved.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // Same zone, other fields - still fine.
        var renamed = await admin.PutAsJsonAsync($"/api/admin/rooms/{room.Id}",
            new SaveRoomRequest("Still Warsaw", room.Capacity, "Europe/Warsaw"));
        renamed.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Changing_a_missing_room_is_404()
    {
        var response = await Admin().PutAsJsonAsync("/api/admin/rooms/999999", NewRoom());

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
