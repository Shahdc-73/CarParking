using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using ParkWise.Application.Auth;

namespace ParkWise.Api.IntegrationTests;

public static class TestHelpers
{
    public const string StrongPassword = "Passw0rd123";

    public static string NewEmail() => $"user-{Guid.NewGuid():N}@test.local";

    /// <summary>
    /// A client that does NOT manage cookies itself, so tests can see and replay the refresh cookie by hand
    /// (that is how we simulate a thief replaying a stolen token).
    /// </summary>
    public static HttpClient CreateRawClient(this ApiFactory factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    public sealed record Session(AuthResponse Body, string RefreshCookie)
    {
        public string AccessToken => Body.AccessToken;
    }

    public static async Task<Session> RegisterAsync(this HttpClient client, string? email = null, string password = StrongPassword)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email ?? NewEmail(), password, "Test User"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadSessionAsync(response);
    }

    public static async Task<Session> LoginAsync(this HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadSessionAsync(response);
    }

    public static async Task<Session> ReadSessionAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<AuthResponse>();
        return new Session(body!, ExtractRefreshCookie(response)!);
    }

    /// <summary>Returns the raw refresh token value from the Set-Cookie header, or null.</summary>
    public static string? ExtractRefreshCookie(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values)) return null;
        var cookie = values.FirstOrDefault(v => v.StartsWith("pw_refresh=", StringComparison.Ordinal));
        return cookie?.Split(';')[0]["pw_refresh=".Length..];
    }

    public static Task<HttpResponseMessage> RefreshAsync(this HttpClient client, string? refreshCookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        if (refreshCookie is not null) request.Headers.Add("Cookie", $"pw_refresh={refreshCookie}");
        return client.SendAsync(request);
    }

    public static HttpClient WithBearer(this HttpClient client, string accessToken)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    public static async Task<string> ProblemCodeAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        return json.GetProperty("code").GetString()!;
    }
}
