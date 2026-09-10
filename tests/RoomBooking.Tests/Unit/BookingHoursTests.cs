using BookingService.Contracts;
using Shouldly;

namespace RoomBooking.Tests.Unit;

public class BookingHoursTests
{
    private static readonly TimeZoneInfo Warsaw = BookingHours.ZoneOf("Europe/Warsaw");
    private static readonly TimeZoneInfo NewYork = BookingHours.ZoneOf("America/New_York");
    private static readonly TimeZoneInfo Utc = BookingHours.ZoneOf("Etc/UTC");

    [Fact]
    public void SlotsOn_returns_one_slot_per_working_hour()
    {
        var slots = BookingHours.SlotsOn(new DateOnly(2026, 9, 11), Warsaw).ToList();

        slots.Count.ShouldBe(BookingHours.LastHour - BookingHours.FirstHour + 1);
        slots.ShouldAllBe(s => s.Kind == DateTimeKind.Utc);
    }

    [Fact]
    public void Slots_are_whole_local_hours_in_the_rooms_zone()
    {
        var slots = BookingHours.SlotsOn(new DateOnly(2026, 9, 11), NewYork).ToList();

        var localHours = slots
            .Select(s => TimeZoneInfo.ConvertTimeFromUtc(s, NewYork).Hour)
            .ToList();

        localHours.First().ShouldBe(BookingHours.FirstHour);
        localHours.Last().ShouldBe(BookingHours.LastHour);
    }

    /// <summary>
    /// The whole reason working hours are not stored in UTC: the same local 08:00
    /// is a different instant in winter and in summer.
    /// </summary>
    [Fact]
    public void The_same_local_hour_maps_to_different_utc_instants_across_daylight_saving()
    {
        var winter = BookingHours.SlotsOn(new DateOnly(2026, 1, 15), Warsaw).First();
        var summer = BookingHours.SlotsOn(new DateOnly(2026, 7, 15), Warsaw).First();

        winter.Hour.ShouldBe(7);   // UTC+1
        summer.Hour.ShouldBe(6);   // UTC+2

        // ...yet both are 08:00 to somebody standing in that room.
        TimeZoneInfo.ConvertTimeFromUtc(winter, Warsaw).Hour.ShouldBe(BookingHours.FirstHour);
        TimeZoneInfo.ConvertTimeFromUtc(summer, Warsaw).Hour.ShouldBe(BookingHours.FirstHour);
    }

    [Fact]
    public void Rooms_in_different_zones_open_at_different_instants()
    {
        var day = new DateOnly(2026, 9, 11);

        var warsaw = BookingHours.SlotsOn(day, Warsaw).First();
        var newYork = BookingHours.SlotsOn(day, NewYork).First();

        newYork.ShouldBeGreaterThan(warsaw);
    }

    [Fact]
    public void Accepts_an_instant_that_is_a_whole_working_hour_locally()
    {
        var slot = BookingHours.SlotsOn(new DateOnly(2026, 9, 11), Warsaw).First();

        BookingHours.IsValidSlot(slot, Warsaw).ShouldBeTrue();
    }

    [Fact]
    public void Rejects_an_instant_that_is_not_on_the_hour()
    {
        var slot = BookingHours.SlotsOn(new DateOnly(2026, 9, 11), Warsaw).First().AddMinutes(30);

        BookingHours.IsValidSlot(slot, Warsaw).ShouldBeFalse();
    }

    [Fact]
    public void Rejects_an_instant_outside_working_hours_in_that_zone()
    {
        var slot = new DateTime(2026, 9, 11, 3, 0, 0, DateTimeKind.Utc);

        BookingHours.IsValidSlot(slot, Warsaw).ShouldBeFalse();
    }

    [Fact]
    public void A_valid_slot_for_one_room_can_be_invalid_for_another_zone()
    {
        var warsawSlot = BookingHours.SlotsOn(new DateOnly(2026, 9, 11), Warsaw).First();

        BookingHours.IsValidSlot(warsawSlot, Warsaw).ShouldBeTrue();
        BookingHours.IsValidSlot(warsawSlot, NewYork).ShouldBeFalse();
    }

    [Fact]
    public void Rejects_a_non_utc_instant()
    {
        var slot = new DateTime(2026, 9, 11, 10, 0, 0, DateTimeKind.Local);

        BookingHours.IsValidSlot(slot, Utc).ShouldBeFalse();
    }
}
