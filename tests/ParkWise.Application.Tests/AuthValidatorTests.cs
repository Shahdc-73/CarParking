using FluentValidation.TestHelper;
using ParkWise.Application.Auth;
using ParkWise.Application.Users;

namespace ParkWise.Application.Tests;

public class AuthValidatorTests
{
    private readonly RegisterRequestValidator _register = new();

    [Fact]
    public void Valid_registration_passes()
        => _register.TestValidate(new RegisterRequest("a@b.com", "Passw0rd1", "Ann")).ShouldNotHaveAnyValidationErrors();

    [Theory]
    [InlineData("short1A")]       // < 8 chars
    [InlineData("alllowercase1")] // no upper
    [InlineData("ALLUPPERCASE1")] // no lower
    [InlineData("NoDigitsHere")]  // no digit
    public void Weak_passwords_are_rejected(string password)
        => _register.TestValidate(new RegisterRequest("a@b.com", password, "Ann"))
            .ShouldHaveValidationErrorFor(x => x.Password);

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    public void Bad_emails_are_rejected(string email)
        => _register.TestValidate(new RegisterRequest(email, "Passw0rd1", "Ann"))
            .ShouldHaveValidationErrorFor(x => x.Email);

    [Fact]
    public void Full_name_is_required()
        => _register.TestValidate(new RegisterRequest("a@b.com", "Passw0rd1", " "))
            .ShouldHaveValidationErrorFor(x => x.FullName);

    [Fact]
    public void Login_requires_email_and_password()
    {
        var result = new LoginRequestValidator().TestValidate(new LoginRequest("", ""));
        result.ShouldHaveValidationErrorFor(x => x.Email);
        result.ShouldHaveValidationErrorFor(x => x.Password);
    }

    [Theory]
    [InlineData("Admin", true)]
    [InlineData("Attendant", true)]
    [InlineData("Driver", true)]
    [InlineData("SuperUser", false)]
    public void Admin_can_only_create_known_roles(string role, bool valid)
    {
        var result = new CreateUserRequestValidator().TestValidate(new CreateUserRequest("a@b.com", "Passw0rd1", "Ann", role));
        if (valid) result.ShouldNotHaveValidationErrorFor(x => x.Role);
        else result.ShouldHaveValidationErrorFor(x => x.Role);
    }
}

public class PagingTests
{
    [Theory]
    [InlineData(0, 0, 1, 1, 0)]
    [InlineData(3, 10, 3, 10, 20)]
    [InlineData(1, 5000, 1, 100, 0)]
    public void Paging_is_clamped_to_safe_values(int page, int size, int expectedPage, int expectedSize, int expectedSkip)
    {
        var q = new Common.PageQuery(page, size);
        Assert.Equal(expectedPage, q.SafePage);
        Assert.Equal(expectedSize, q.SafePageSize);
        Assert.Equal(expectedSkip, q.Skip);
    }
}
