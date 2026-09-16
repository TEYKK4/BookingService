using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using RoomBooking.Contracts;
using RoomBooking.Data;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace RoomBooking.Tests.Integration;

public class BookingApiTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private ApiFactory _factory = null!;

    private const int Alice = 1;
    private const int Bob = 2;

    public Task InitializeAsync()
    {
        _factory = new ApiFactory(postgres.ConnectionString);

        // Touching Services builds the host, which runs migrations. Without this
        // a test that writes straight to the database would depend on some other
        // test having created a client first.
        _ = _factory.Services;

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Writes straight to the database: the API refuses to create a booking in
    /// the past, but history has to be set up somehow.
    /// </summary>
    private async Task<int> GivenAPastBooking(int userId, int roomId)
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(postgres.ConnectionString)
            .Options);

        var zone = BookingHours.ZoneOf("Europe/Warsaw");
        var slot = BookingHours.SlotsOn(BookingHours.TodayIn(zone).AddDays(-3), zone)
            .ElementAt(Interlocked.Increment(ref _hourOffset) % 12);

        var booking = new RoomBooking.Models.Booking
        {
            RoomId = roomId, UserId = userId, SlotStart = slot, CreatedAt = DateTime.UtcNow,
        };

        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        return booking.Id;
    }

    private HttpClient ClientFor(int? userId = null)
    {
        var client = _factory.CreateClient();

        if (userId is { } id)
        {
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", TestJwt.ForUser(id));
        }

        return client;
    }

    private static int _hourOffset;

    /// <summary>
    /// A fresh future slot for a room, taken from that room's own working hours
    /// so the test agrees with the server about what a valid slot is.
    /// </summary>
    private static DateTime NextFreeSlot(string timeZoneId = "Europe/Warsaw")
    {
        var offset = Interlocked.Increment(ref _hourOffset);
        var zone = BookingHours.ZoneOf(timeZoneId);
        var day = BookingHours.TodayIn(zone).AddDays(1 + offset / 12);

        return BookingHours.SlotsOn(day, zone).ElementAt(offset % 12);
    }

    [Fact]
    public async Task Rooms_are_visible_without_signing_in()
    {
        var rooms = await ClientFor().GetFromJsonAsync<List<RoomResponse>>("/api/rooms");

        rooms.ShouldNotBeNull();
        rooms.Count.ShouldBe(4);
    }

    [Fact]
    public async Task Booking_without_a_token_is_rejected()
    {
        var response = await ClientFor().PostAsJsonAsync("/api/bookings",
            new CreateBookingRequest(1, NextFreeSlot()));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Booking_a_free_slot_succeeds()
    {
        var response = await ClientFor(Alice).PostAsJsonAsync("/api/bookings",
            new CreateBookingRequest(1, NextFreeSlot()));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var booking = await response.Content.ReadFromJsonAsync<BookingResponse>();
        booking.ShouldNotBeNull();
        booking.RoomName.ShouldBe("Focus");
    }

    [Fact]
    public async Task The_same_slot_cannot_be_booked_twice()
    {
        var slot = NextFreeSlot();
        var client = ClientFor(Alice);

        (await client.PostAsJsonAsync("/api/bookings", new CreateBookingRequest(2, slot)))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var second = await ClientFor(Bob).PostAsJsonAsync("/api/bookings", new CreateBookingRequest(2, slot));

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Only_one_of_many_simultaneous_bookings_wins()
    {
        var slot = NextFreeSlot();

        var attempts = Enumerable.Range(0, 8).Select(i =>
            ClientFor(i + 10).PostAsJsonAsync("/api/bookings", new CreateBookingRequest(3, slot)));

        var responses = await Task.WhenAll(attempts);

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBe(7);
    }

    [Fact]
    public async Task A_slot_that_is_not_on_the_hour_is_rejected()
    {
        var slot = NextFreeSlot().AddMinutes(30);

        var response = await ClientFor(Alice).PostAsJsonAsync("/api/bookings",
            new CreateBookingRequest(1, slot));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_hour_outside_the_rooms_working_day_is_rejected()
    {
        // 03:00 UTC is the middle of the night in every seeded room.
        var slot = DateTime.SpecifyKind(
            DateTime.UtcNow.Date.AddDays(2).AddHours(3), DateTimeKind.Utc);

        var response = await ClientFor(Alice).PostAsJsonAsync("/api/bookings",
            new CreateBookingRequest(1, slot));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_slot_valid_for_a_warsaw_room_is_refused_by_a_new_york_room()
    {
        // Room 4 sits in America/New_York, so Warsaw's 08:00 is the small hours there.
        var warsawMorning = NextFreeSlot("Europe/Warsaw");

        var response = await ClientFor(Alice).PostAsJsonAsync("/api/bookings",
            new CreateBookingRequest(4, warsawMorning));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }


    [Fact]
    public async Task A_slot_in_the_past_is_rejected()
    {
        var zone = BookingHours.ZoneOf("Europe/Warsaw");
        var slot = BookingHours.SlotsOn(BookingHours.TodayIn(zone).AddDays(-1), zone).First();

        var response = await ClientFor(Alice).PostAsJsonAsync("/api/bookings",
            new CreateBookingRequest(1, slot));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task My_bookings_leaves_out_hours_that_have_already_passed()
    {
        const int carol = 41;
        var pastId = await GivenAPastBooking(carol, roomId: 1);

        var upcoming = await ClientFor(carol).GetFromJsonAsync<List<BookingResponse>>("/api/bookings/my");

        upcoming.ShouldNotBeNull();
        upcoming.ShouldNotContain(b => b.Id == pastId);
    }

    [Fact]
    public async Task Past_bookings_can_still_be_asked_for_explicitly()
    {
        const int dave = 42;
        var pastId = await GivenAPastBooking(dave, roomId: 2);

        var history = await ClientFor(dave)
            .GetFromJsonAsync<List<BookingResponse>>("/api/bookings/my?scope=past");

        history.ShouldNotBeNull();
        history.ShouldContain(b => b.Id == pastId);
    }

    [Fact]
    public async Task An_unknown_scope_is_rejected_rather_than_ignored()
    {
        var response = await ClientFor(Alice).GetAsync("/api/bookings/my?scope=nonsense");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }


    [Fact]
    public async Task My_bookings_only_lists_my_own()
    {
        var slot = NextFreeSlot();
        await ClientFor(Alice).PostAsJsonAsync("/api/bookings", new CreateBookingRequest(4, slot));

        var bobsBookings = await ClientFor(Bob).GetFromJsonAsync<List<BookingResponse>>("/api/bookings/my");

        bobsBookings.ShouldNotBeNull();
        bobsBookings.ShouldNotContain(b => b.SlotStart == slot);
    }

    [Fact]
    public async Task Cancelling_my_own_booking_works()
    {
        var created = await ClientFor(Alice).PostAsJsonAsync("/api/bookings",
            new CreateBookingRequest(1, NextFreeSlot()));
        var booking = await created.Content.ReadFromJsonAsync<BookingResponse>();

        var response = await ClientFor(Alice).DeleteAsync($"/api/bookings/{booking!.Id}");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Cancelling_someone_elses_booking_looks_like_it_does_not_exist()
    {
        var created = await ClientFor(Alice).PostAsJsonAsync("/api/bookings",
            new CreateBookingRequest(1, NextFreeSlot()));
        var booking = await created.Content.ReadFromJsonAsync<BookingResponse>();

        var response = await ClientFor(Bob).DeleteAsync($"/api/bookings/{booking!.Id}");

        // 404 rather than 403: a 403 would confirm that this booking exists.
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Availability_marks_a_booked_slot_as_taken_and_mine()
    {
        var slot = NextFreeSlot();
        await ClientFor(Alice).PostAsJsonAsync("/api/bookings", new CreateBookingRequest(2, slot));

        var date = DateOnly.FromDateTime(slot).ToString("yyyy-MM-dd");
        var slots = await ClientFor(Alice)
            .GetFromJsonAsync<List<SlotResponse>>($"/api/rooms/2/availability?date={date}");

        var booked = slots.ShouldNotBeNull().Single(s => s.SlotStart == slot);
        booked.IsTaken.ShouldBeTrue();
        booked.IsMine.ShouldBeTrue();
    }
}
