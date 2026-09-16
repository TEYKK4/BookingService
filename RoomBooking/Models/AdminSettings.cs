namespace RoomBooking.Models;

/// <summary>
/// The first admin account, created at startup from configuration so that a
/// fresh deployment has someone who can manage rooms. Bound from "Admin".
/// </summary>
public class AdminSettings
{
    public string Login { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
