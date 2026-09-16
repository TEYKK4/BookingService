namespace RoomBooking.Models;

public class Room
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Capacity { get; set; }

    /// <summary>IANA time zone of the office this room is in, e.g. "Europe/Warsaw".</summary>
    public string TimeZoneId { get; set; } = "Etc/UTC";

    /// <summary>
    /// Rooms are deactivated, never deleted: deleting would cascade to every
    /// booking ever made in it. Inactive rooms are hidden and cannot be booked.
    /// </summary>
    public bool IsActive { get; set; } = true;

    public List<Booking> Bookings { get; set; } = [];
}
