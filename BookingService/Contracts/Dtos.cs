namespace BookingService.Contracts;

public record CredentialsRequest(string Login, string Password);

public record TokenResponse(string Token);

public record RoomResponse(int Id, string Name, int Capacity, string TimeZoneId);

/// <summary><paramref name="SlotStart"/> is a UTC instant; render it in the room's zone.</summary>
public record SlotResponse(DateTime SlotStart, bool IsTaken, bool IsMine);

public record CreateBookingRequest(int RoomId, DateTime SlotStart);

public record BookingResponse(
    int Id, int RoomId, string RoomName, string TimeZoneId, DateTime SlotStart, DateTime CreatedAt);
