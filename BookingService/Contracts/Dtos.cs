namespace BookingService.Contracts;

public record CredentialsRequest(string Login, string Password);

public record TokenResponse(string Token);

public record RoomResponse(int Id, string Name, int Capacity);

public record SlotResponse(DateTime SlotStart, bool IsTaken, bool IsMine);

public record CreateBookingRequest(int RoomId, DateTime SlotStart);

public record BookingResponse(int Id, int RoomId, string RoomName, DateTime SlotStart, DateTime CreatedAt);
