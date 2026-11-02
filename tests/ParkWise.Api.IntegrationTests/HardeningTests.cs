using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace ParkWise.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class HardeningTests(ApiFactory factory)
{
    private static readonly object BadLogin = new { email = "nobody@parkwise.local", password = "Wrong-password-1" };

    /// <summary>A host with a tiny limit. A test header fakes the TCP peer's address, which TestServer does not provide.</summary>
    private HttpClient LimitedClient(int permit, string? trustedNetwork = null) => factory.WithWebHostBuilder(b =>
    {
        b.UseSetting("RateLimiting:Auth:PermitLimit", permit.ToString());
        if (trustedNetwork is not null) b.UseSetting("ForwardedHeaders:KnownNetworks:0", trustedNetwork);
        b.ConfigureServices(s => s.AddTransient<IStartupFilter, FakePeerFilter>());
    }).CreateClient();

    private sealed class FakePeerFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((ctx, n) =>
            {
                if (ctx.Request.Headers.TryGetValue("X-Test-Peer", out var peer))
                    ctx.Connection.RemoteIpAddress = IPAddress.Parse(peer.ToString());
                return n();
            });
            next(app);
        };
    }

    private static HttpRequestMessage Login(string peer, string? forwardedFor = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login") { Content = JsonContent.Create(BadLogin) };
        request.Headers.Add("X-Test-Peer", peer);
        if (forwardedFor is not null) request.Headers.Add("X-Forwarded-For", forwardedFor);
        return request;
    }

    [Fact]
    public async Task Too_many_auth_requests_get_429_with_retry_after_and_a_problem_body()
    {
        var client = LimitedClient(3);
        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(Login("10.0.0.1"))).StatusCode);

        var blocked = await client.SendAsync(Login("10.0.0.1"));

        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.True(blocked.Headers.RetryAfter?.Delta?.TotalSeconds > 0);
        Assert.Equal("application/problem+json", blocked.Content.Headers.ContentType?.MediaType);
        Assert.Contains("rate_limit.exceeded", await blocked.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task One_client_being_blocked_does_not_block_another()
    {
        var client = LimitedClient(2);
        for (var i = 0; i < 3; i++) await client.SendAsync(Login("10.0.0.1"));

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(Login("10.0.0.2"))).StatusCode);
    }

    [Fact]
    public async Task Behind_a_trusted_proxy_each_real_client_has_its_own_budget()
    {
        var client = LimitedClient(2, trustedNetwork: "172.16.0.0/12");
        // Same proxy address for everybody; only X-Forwarded-For tells clients apart.
        for (var i = 0; i < 3; i++) await client.SendAsync(Login("172.18.0.5", "203.0.113.1"));

        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.SendAsync(Login("172.18.0.5", "203.0.113.1"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(Login("172.18.0.5", "203.0.113.2"))).StatusCode);
    }

    [Fact]
    public async Task A_forwarded_for_header_from_an_untrusted_peer_cannot_dodge_the_limit()
    {
        var client = LimitedClient(2, trustedNetwork: "172.16.0.0/12");
        // The peer is NOT the proxy, so it cannot claim to be a different client on every request.
        for (var i = 0; i < 3; i++) await client.SendAsync(Login("198.51.100.7", $"203.0.113.{i + 10}"));

        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.SendAsync(Login("198.51.100.7", "203.0.113.99"))).StatusCode);
    }

    [Fact]
    public async Task Other_endpoints_are_not_rate_limited_like_auth()
    {
        var client = LimitedClient(1);
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
    }

    [Fact]
    public async Task Auth_responses_are_never_cached_and_every_response_forbids_sniffing()
    {
        var auth = await factory.CreateRawClient().PostAsJsonAsync("/api/v1/auth/login", BadLogin);
        Assert.Contains("no-store", auth.Headers.CacheControl?.ToString());
        Assert.Contains("nosniff", auth.Headers.GetValues("X-Content-Type-Options"));
        Assert.Contains("no-referrer", auth.Headers.GetValues("Referrer-Policy"));

        var health = await factory.CreateRawClient().GetAsync("/health");
        Assert.Contains("nosniff", health.Headers.GetValues("X-Content-Type-Options"));
    }
}
