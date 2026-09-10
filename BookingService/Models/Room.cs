namespace BookingService.Models;

public class Room
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Capacity { get; set; }

    public List<Booking> Bookings { get; set; } = [];
}
