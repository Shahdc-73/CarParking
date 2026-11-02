using System.Net;
using System.Net.Http.Json;
using ParkWise.Application.Auth;
using ParkWise.Application.Common;
using ParkWise.Application.Users;
using static ParkWise.Api.IntegrationTests.TestHelpers;

namespace ParkWise.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class UsersAndRolesTests(ApiFactory factory)
{
    private HttpClient Client => factory.CreateRawClient();

    private async Task<HttpClient> AdminClientAsync()
    {
        var session = await Client.LoginAsync("admin@parkwise.local", ApiFactory.SeedPassword);
        return Client.WithBearer(session.AccessToken);
    }

    [Theory]
    [InlineData("admin@parkwise.local", "Admin")]
    [InlineData("attendant@parkwise.local", "Attendant")]
    [InlineData("driver1@parkwise.local", "Driver")]
    [InlineData("driver2@parkwise.local", "Driver")]
    public async Task Seeded_users_can_log_in_with_their_role(string email, string role)
    {
        var session = await Client.LoginAsync(email, ApiFactory.SeedPassword);
        Assert.Equal([role], session.Body.User.Roles);
    }

    [Fact]
    public async Task Listing_users_requires_authentication()
        => Assert.Equal(HttpStatusCode.Unauthorized, (await Client.GetAsync("/api/v1/users")).StatusCode);

    [Theory]
    [InlineData("driver1@parkwise.local")]
    [InlineData("attendant@parkwise.local")]
    public async Task Non_admins_get_403_on_admin_endpoints(string email)
    {
        var session = await Client.LoginAsync(email, ApiFactory.SeedPassword);
        var response = await Client.WithBearer(session.AccessToken).GetAsync("/api/v1/users");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_can_list_users_with_paging_search_and_role_filter()
    {
        var admin = await AdminClientAsync();

        var drivers = await admin.GetFromJsonAsync<PagedResult<UserDto>>("/api/v1/users?role=Driver&search=parkwise.local&pageSize=1&sort=email");

        Assert.Equal(1, drivers!.PageSize);
        Assert.Single(drivers.Items);
        Assert.True(drivers.TotalCount >= 2);
        Assert.Equal("driver1@parkwise.local", drivers.Items[0].Email);
        Assert.Contains("Driver", drivers.Items[0].Roles);
    }

    [Fact]
    public async Task Admin_can_create_an_attendant_who_can_then_log_in()
    {
        var admin = await AdminClientAsync();
        var email = NewEmail();

        var created = await admin.PostAsJsonAsync("/api/v1/users", new CreateUserRequest(email, StrongPassword, "New Attendant", Roles.Attendant));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var session = await Client.LoginAsync(email, StrongPassword);
        Assert.Equal(["Attendant"], session.Body.User.Roles);
    }

    [Fact]
    public async Task Creating_a_user_with_an_unknown_role_returns_400()
    {
        var admin = await AdminClientAsync();
        var response = await admin.PostAsJsonAsync("/api/v1/users", new CreateUserRequest(NewEmail(), StrongPassword, "X", "SuperUser"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Self_registration_can_never_produce_a_privileged_role()
    {
        // Even if a client smuggles extra fields into the body, the DTO has no Role field.
        var response = await Client.PostAsJsonAsync("/api/v1/auth/register",
            new { email = NewEmail(), password = StrongPassword, fullName = "Sneaky", role = "Admin", roles = new[] { "Admin" } });

        var session = await ReadSessionAsync(response);
        Assert.Equal(["Driver"], session.Body.User.Roles);
    }

    [Fact]
    public async Task Deactivating_a_user_blocks_login_and_kills_existing_refresh_tokens()
    {
        var admin = await AdminClientAsync();
        var email = NewEmail();
        var session = await Client.RegisterAsync(email);

        var response = await admin.PutAsJsonAsync($"/api/v1/users/{session.Body.User.Id}/active", new { isActive = false });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var login = await Client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, StrongPassword));
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.RefreshAsync(session.RefreshCookie)).StatusCode);
    }

    [Fact]
    public async Task Deactivating_an_unknown_user_returns_404()
    {
        var admin = await AdminClientAsync();
        var response = await admin.PutAsJsonAsync($"/api/v1/users/{Guid.NewGuid()}/active", new { isActive = false });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
