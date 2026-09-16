using FluentValidation;
using RoomBooking.Contracts;

namespace RoomBooking.Validators;

public class SaveRoomValidator : AbstractValidator<SaveRoomRequest>
{
    public SaveRoomValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required")
            .MaximumLength(100).WithMessage("Name must not exceed 100 characters");

        RuleFor(x => x.Capacity)
            .InclusiveBetween(1, 1000).WithMessage("Capacity must be between 1 and 1000");

        RuleFor(x => x.TimeZoneId)
            // Stop after the first failure: BeAKnownZone must never see a null id.
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Time zone is required")
            .Must(BeAKnownZone).WithMessage(x => $"'{x.TimeZoneId}' is not a known IANA time zone, e.g. Europe/Warsaw");
    }

    /// <summary>
    /// The same resolver the app uses at startup, so a zone accepted here is a zone
    /// the runtime can actually work with - tzdata and all.
    /// </summary>
    private static bool BeAKnownZone(string timeZoneId)
    {
        try
        {
            BookingHours.ZoneOf(timeZoneId);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
