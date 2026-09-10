using AuthGrpcService;
using AuthGrpcService.Validators;
using Shouldly;

namespace RoomBooking.Tests.Unit;

public class CredentialsValidatorTests
{
    private readonly CredentialsValidator _validator = new();

    [Theory]
    [InlineData("bob", "secret123")]
    [InlineData("abc", "123456")]
    public void Accepts_valid_credentials(string login, string password)
    {
        var result = _validator.Validate(new Credentials { Login = login, Password = password });

        result.IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("", "secret123")]
    [InlineData("ab", "secret123")]
    public void Rejects_bad_login(string login, string password)
    {
        var result = _validator.Validate(new Credentials { Login = login, Password = password });

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(Credentials.Login));
    }

    [Theory]
    [InlineData("bob", "")]
    [InlineData("bob", "12345")]
    public void Rejects_short_password(string login, string password)
    {
        var result = _validator.Validate(new Credentials { Login = login, Password = password });

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(Credentials.Password));
    }

    [Fact]
    public void Rejects_login_over_50_characters()
    {
        var result = _validator.Validate(new Credentials { Login = new string('a', 51), Password = "secret123" });

        result.IsValid.ShouldBeFalse();
    }
}
