namespace RoomBooking.Contracts;

public record CredentialsRequest(string Login, string Password);

public record TokenResponse(string Token);

public record MeResponse(int Id, string Login, string Role);

public record RoomResponse(int Id, string Name, int Capacity, string TimeZoneId);

/// <summary><paramref name="SlotStart"/> is a UTC instant; render it in the room's zone.</summary>
public record SlotResponse(DateTime SlotStart, bool IsTaken, bool IsMine);

public record CreateBookingRequest(int RoomId, DateTime SlotStart);

/// <summary>What an admin sees: includes deactivated rooms and the flag itself.</summary>
public record AdminRoomResponse(int Id, string Name, int Capacity, string TimeZoneId, bool IsActive);

public record SaveRoomRequest(string Name, int Capacity, string TimeZoneId);

/// <summary>A booking as an admin sees it: whose it is, not just where and when.</summary>
public record AdminBookingResponse(
    int Id, int RoomId, string RoomName, string TimeZoneId, DateTime SlotStart,
    int UserId, string UserLogin, DateTime CreatedAt);

public record AdminUserResponse(int Id, string Login, string Role);

public record SetRoleRequest(string Role);

public record BookingResponse(
    int Id, int RoomId, string RoomName, string TimeZoneId, DateTime SlotStart, DateTime CreatedAt);

public enum BookingScope
{
    Upcoming,
    Past,
    All,
}
