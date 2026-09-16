namespace RoomBooking.Contracts;

public static class BookingScopes
{
    /// <summary>
    /// Parsed by hand because minimal APIs bind enums case-sensitively, and
    /// "?scope=past" is what a caller naturally writes. Missing means upcoming.
    /// </summary>
    public static bool TryParse(string? scope, out BookingScope result) =>
        Enum.TryParse(scope ?? nameof(BookingScope.Upcoming), ignoreCase: true, out result);

    public static IResult InvalidScopeProblem() =>
        Results.Problem(
            $"scope must be one of: {string.Join(", ", Enum.GetNames<BookingScope>()).ToLowerInvariant()}.",
            statusCode: StatusCodes.Status400BadRequest);
}
