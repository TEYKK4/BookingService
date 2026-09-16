namespace RoomBooking.Models;

public class Booking
{
    public int Id { get; set; }

    public int RoomId { get; set; }
    public Room Room { get; set; } = null!;

    /// <summary>Id of the user from the JWT. No foreign key - users live in another service.</summary>
    public int UserId { get; set; }

    /// <summary>Start of the one-hour slot, always UTC and always on the hour.</summary>
    public DateTime SlotStart { get; set; }

    public DateTime CreatedAt { get; set; }
}
