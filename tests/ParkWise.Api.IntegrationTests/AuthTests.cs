using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ParkWise.Application.Auth;
using static ParkWise.Api.IntegrationTests.TestHelpers;

namespace ParkWise.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class AuthTests(ApiFactory factory)
{
    private HttpClient Client => factory.CreateRawClient();

    [Fact]
    public async Task Register_creates_a_driver_and_sets_an_httponly_refresh_cookie()
    {
        var response = await Client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(NewEmail(), StrongPassword, "Ann"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.Equal(["Driver"], body!.User.Roles);
        Assert.False(string.IsNullOrEmpty(body.AccessToken));

        var setCookie = response.Headers.GetValues("Set-Cookie").Single(v => v.StartsWith("pw_refresh="));
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/v1/auth", setCookie, StringComparison.OrdinalIgnoreCase);

        // The refresh token must never appear in the JSON body.
        Assert.DoesNotContain("refresh", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Register_with_existing_email_returns_409()
    {
        var email = NewEmail();
        await Client.RegisterAsync(email);

        var response = await Client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, StrongPassword, "Again"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("auth.email_taken", await ProblemCodeAsync(response));
    }

    [Fact]
    public async Task Register_with_weak_password_returns_400_with_field_errors()
    {
        var response = await Client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest("not-an-email", "weak", ""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        Assert.True(errors.TryGetProperty("email", out _));
        Assert.True(errors.TryGetProperty("password", out _));
        Assert.True(errors.TryGetProperty("fullName", out _));
    }

    [Fact]
    public async Task Login_with_wrong_password_and_unknown_email_give_the_same_401()
    {
        var email = NewEmail();
        await Client.RegisterAsync(email);

        var wrongPassword = await Client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "Wr0ngPassword"));
        var unknownEmail = await Client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(NewEmail(), StrongPassword));

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmail.StatusCode);
        Assert.Equal(await ProblemCodeAsync(wrongPassword), await ProblemCodeAsync(unknownEmail)); // no account enumeration
    }

    [Fact]
    public async Task Me_requires_a_token_and_returns_the_user_with_one()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.GetAsync("/api/v1/auth/me")).StatusCode);

        var email = NewEmail();
        var session = await Client.RegisterAsync(email);
        var me = await Client.WithBearer(session.AccessToken).GetFromJsonAsync<UserDto>("/api/v1/auth/me");

        Assert.Equal(email, me!.Email);
    }

    [Fact]
    public async Task A_tampered_token_is_rejected()
    {
        var session = await Client.RegisterAsync();
        var tampered = session.AccessToken[..^3] + "abc";

        var response = await Client.WithBearer(tampered).GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_rotates_the_token_and_issues_a_new_access_token()
    {
        var client = Client;
        var session = await client.RegisterAsync();

        var response = await client.RefreshAsync(session.RefreshCookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rotated = await ReadSessionAsync(response);
        Assert.NotEqual(session.RefreshCookie, rotated.RefreshCookie);
        Assert.Equal(HttpStatusCode.OK, (await client.WithBearer(rotated.AccessToken).GetAsync("/api/v1/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Refresh_without_a_cookie_returns_401()
        => Assert.Equal(HttpStatusCode.Unauthorized, (await Client.RefreshAsync(null)).StatusCode);

    [Fact]
    public async Task Replaying_a_used_refresh_token_revokes_the_whole_family()
    {
        var client = Client;
        var original = await client.RegisterAsync();

        var firstRefresh = await client.RefreshAsync(original.RefreshCookie);   // legitimate rotation
        var rotated = await ReadSessionAsync(firstRefresh);

        var replay = await client.RefreshAsync(original.RefreshCookie);         // thief replays the old token
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);

        // The newest token in the family is now dead too: the real user must log in again.
        var afterTheft = await client.RefreshAsync(rotated.RefreshCookie);
        Assert.Equal(HttpStatusCode.Unauthorized, afterTheft.StatusCode);
    }

    [Fact]
    public async Task Concurrent_refreshes_of_the_same_token_let_exactly_one_win()
    {
        var client = Client;
        var session = await client.RegisterAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => client.RefreshAsync(session.RefreshCookie)));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(7, responses.Count(r => r.StatusCode == HttpStatusCode.Unauthorized));
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token_and_is_idempotent()
    {
        var client = Client;
        var session = await client.RegisterAsync();

        for (var i = 0; i < 2; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
            request.Headers.Add("Cookie", $"pw_refresh={session.RefreshCookie}");
            Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(request)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.RefreshAsync(session.RefreshCookie)).StatusCode);
    }

    [Fact]
    public async Task Five_failed_logins_lock_the_account_even_for_the_correct_password()
    {
        var email = NewEmail();
        await Client.RegisterAsync(email);

        for (var i = 0; i < 5; i++)
            await Client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "Wr0ngPassword"));

        var response = await Client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, StrongPassword));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("auth.locked_out", await ProblemCodeAsync(response));
    }
}
