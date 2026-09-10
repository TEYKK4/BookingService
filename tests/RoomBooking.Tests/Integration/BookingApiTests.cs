using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BookingService.Contracts;
using Shouldly;

namespace RoomBooking.Tests.Integration;

public class BookingApiTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private BookingApiFactory _factory = null!;

    private const int Alice = 1;
    private const int Bob = 2;

    public Task InitializeAsync()
    {
        _factory = new BookingApiFactory(postgres.ConnectionString);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
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

    /// <summary>A fresh future slot, so tests never collide with each other.</summary>
    private static DateTime NextFreeSlot()
    {
        var offset = Interlocked.Increment(ref _hourOffset);
        var day = DateTime.UtcNow.Date.AddDays(1 + offset / 12);
        return DateTime.SpecifyKind(day.AddHours(BookingHours.FirstHour + offset % 12), DateTimeKind.Utc);
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

    [Theory]
    [InlineData(30)]   // not on the hour
    [InlineData(0)]
    public async Task Slots_must_be_whole_hours_inside_working_hours(int minutes)
    {
        var slot = DateTime.SpecifyKind(
            DateTime.UtcNow.Date.AddDays(2).AddHours(3).AddMinutes(minutes), DateTimeKind.Utc);

        var response = await ClientFor(Alice).PostAsJsonAsync("/api/bookings",
            new CreateBookingRequest(1, slot));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_slot_in_the_past_is_rejected()
    {
        var slot = DateTime.SpecifyKind(
            DateTime.UtcNow.Date.AddDays(-1).AddHours(BookingHours.FirstHour), DateTimeKind.Utc);

        var response = await ClientFor(Alice).PostAsJsonAsync("/api/bookings",
            new CreateBookingRequest(1, slot));

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
