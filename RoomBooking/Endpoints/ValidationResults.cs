using FluentValidation.Results;

namespace RoomBooking.Endpoints;

public static class ValidationResults
{
    /// <summary>One 400 carrying every message, comma-separated - the same shape on every endpoint.</summary>
    public static IResult ToProblem(this ValidationResult result) =>
        Results.Problem(
            string.Join(", ", result.Errors.Select(e => e.ErrorMessage)),
            statusCode: StatusCodes.Status400BadRequest);
}
