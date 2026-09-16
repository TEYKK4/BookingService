using System.Collections.Concurrent;

namespace RoomBooking.Contracts;

/// <summary>
/// Bookings are fixed one-hour slots, and working hours belong to the room's own
/// time zone - not to UTC. A range hardcoded in UTC would silently shift by an
/// hour twice a year, because a zone's offset changes with daylight saving.
/// Instants are still stored and compared in UTC.
/// </summary>
public static class BookingHours
{
    public const int FirstHour = 8;
    public const int LastHour = 19;
    public const int DaysBookableAhead = 30;

    private static readonly ConcurrentDictionary<string, TimeZoneInfo> Zones = new();

    /// <summary>Resolves an IANA id such as "Europe/Warsaw". Cached - the lookup is not free.</summary>
    /// <exception cref="InvalidOperationException">
    /// The id is not a time zone this machine knows. Usually a typo in seed data,
    /// or a container image without tzdata installed.
    /// </exception>
    public static TimeZoneInfo ZoneOf(string timeZoneId)
    {
        if (Zones.TryGetValue(timeZoneId, out var cached))
        {
            return cached;
        }

        try
        {
            return Zones.GetOrAdd(timeZoneId, TimeZoneInfo.FindSystemTimeZoneById);
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new InvalidOperationException(
                $"'{timeZoneId}' is not a known IANA time zone on this system. " +
                "Check the room's TimeZoneId, and that the runtime image has tzdata.", e);
        }
    }

    /// <summary>The UTC instants of every bookable hour on one local day.</summary>
    public static IEnumerable<DateTime> SlotsOn(DateOnly localDate, TimeZoneInfo zone)
    {
        for (var hour = FirstHour; hour <= LastHour; hour++)
        {
            var local = localDate.ToDateTime(new TimeOnly(hour, 0));

            // Swallowed by a spring-forward transition: this local hour never happens.
            if (zone.IsInvalidTime(local))
            {
                continue;
            }

            yield return TimeZoneInfo.ConvertTimeToUtc(local, zone);
        }
    }

    /// <summary>True when the instant lands on a whole working hour in that zone.</summary>
    public static bool IsValidSlot(DateTime utcSlot, TimeZoneInfo zone)
    {
        if (utcSlot.Kind != DateTimeKind.Utc)
        {
            return false;
        }

        var local = TimeZoneInfo.ConvertTimeFromUtc(utcSlot, zone);

        return local.TimeOfDay == TimeSpan.FromHours(local.Hour) && local.Hour is >= FirstHour and <= LastHour;
    }

    public static DateOnly TodayIn(TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone));
}
