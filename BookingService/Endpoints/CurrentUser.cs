using System.Security.Claims;

namespace BookingService.Endpoints;

public static class CurrentUser
{
    /// <summary>
    /// The user id comes from the signed token, never from the request body -
    /// otherwise anyone could act as anyone by sending a different id.
    /// </summary>
    public static int? IdOrNull(this ClaimsPrincipal principal)
    {
        var claim = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(claim, out var id) ? id : null;
    }
}
