using BookingService.Contracts;
using Shouldly;

namespace RoomBooking.Tests.Unit;

public class BookingHoursTests
{
    [Fact]
    public void SlotsOn_returns_one_slot_per_working_hour()
    {
        var slots = BookingHours.SlotsOn(new DateOnly(2026, 9, 11)).ToList();

        slots.Count.ShouldBe(BookingHours.LastHour - BookingHours.FirstHour + 1);
        slots.First().Hour.ShouldBe(BookingHours.FirstHour);
        slots.Last().Hour.ShouldBe(BookingHours.LastHour);
        slots.ShouldAllBe(s => s.Kind == DateTimeKind.Utc);
    }

    [Fact]
    public void Accepts_a_whole_hour_inside_working_hours()
    {
        var slot = new DateTime(2026, 9, 11, 10, 0, 0, DateTimeKind.Utc);

        BookingHours.IsValidSlot(slot).ShouldBeTrue();
    }

    [Fact]
    public void Rejects_a_slot_that_is_not_on_the_hour()
    {
        var slot = new DateTime(2026, 9, 11, 10, 30, 0, DateTimeKind.Utc);

        BookingHours.IsValidSlot(slot).ShouldBeFalse();
    }

    [Theory]
    [InlineData(7)]
    [InlineData(20)]
    [InlineData(23)]
    public void Rejects_slots_outside_working_hours(int hour)
    {
        var slot = new DateTime(2026, 9, 11, hour, 0, 0, DateTimeKind.Utc);

        BookingHours.IsValidSlot(slot).ShouldBeFalse();
    }

    [Fact]
    public void Rejects_a_slot_that_is_not_utc()
    {
        var slot = new DateTime(2026, 9, 11, 10, 0, 0, DateTimeKind.Local);

        BookingHours.IsValidSlot(slot).ShouldBeFalse();
    }
}
