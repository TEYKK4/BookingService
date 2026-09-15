namespace RoomBooking.Models;

public class Room
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Capacity { get; set; }

    /// <summary>IANA time zone of the office this room is in, e.g. "Europe/Warsaw".</summary>
    public string TimeZoneId { get; set; } = "Etc/UTC";

    public List<Booking> Bookings { get; set; } = [];
}
