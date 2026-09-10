namespace BookingService.Contracts;

/// <summary>
/// Bookings are fixed one-hour slots. That is what lets a single unique index
/// on (RoomId, SlotStart) prevent double booking - with arbitrary time ranges
/// we would need overlap detection instead.
/// </summary>
public static class BookingHours
{
    public const int FirstHour = 8;
    public const int LastHour = 19;
    public const int DaysBookableAhead = 30;

    public static IEnumerable<DateTime> SlotsOn(DateOnly date)
    {
        for (var hour = FirstHour; hour <= LastHour; hour++)
        {
            yield return new DateTime(date.Year, date.Month, date.Day, hour, 0, 0, DateTimeKind.Utc);
        }
    }

    public static bool IsValidSlot(DateTime slotStart) =>
        slotStart.Kind == DateTimeKind.Utc
        && slotStart == new DateTime(slotStart.Year, slotStart.Month, slotStart.Day, slotStart.Hour, 0, 0, DateTimeKind.Utc)
        && slotStart.Hour >= FirstHour
        && slotStart.Hour <= LastHour;
}
