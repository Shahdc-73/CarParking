using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using ParkWise.Infrastructure.Identity;

namespace ParkWise.Infrastructure.Tests;

public class JwtTokenServiceTests
{
    private const string Key = "unit-test-signing-key-at-least-32-characters-long!";
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static (JwtTokenService Service, FakeTimeProvider Clock, JwtOptions Options) Create()
    {
        var options = new JwtOptions { Key = Key, AccessTokenMinutes = 15 };
        var clock = new FakeTimeProvider(Start);
        return (new JwtTokenService(Options.Create(options), clock), clock, options);
    }

    private static ApplicationUser User() => new() { Id = Guid.NewGuid(), Email = "a@b.com", FullName = "Ann" };

    private static TokenValidationParameters Parameters(JwtOptions o, string key, TimeProvider clock) => new()
    {
        ValidIssuer = o.Issuer,
        ValidAudience = o.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
        LifetimeValidator = (notBefore, expires, _, _) =>
            notBefore <= clock.GetUtcNow().UtcDateTime && clock.GetUtcNow().UtcDateTime < expires,
    };

    [Fact]
    public async Task Access_token_carries_subject_email_name_and_all_roles()
    {
        var (service, clock, options) = Create();
        var user = User();

        var (token, expires) = service.CreateAccessToken(user, ["Admin", "Attendant"]);
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, Parameters(options, Key, clock));

        Assert.True(result.IsValid);
        var jwt = (JsonWebToken)result.SecurityToken;
        Assert.Equal(user.Id.ToString(), jwt.Subject);
        Assert.Equal("a@b.com", jwt.GetClaim("email").Value);
        Assert.Equal(["Admin", "Attendant"], jwt.Claims.Where(c => c.Type == "role").Select(c => c.Value).Order());
        Assert.Equal(Start.UtcDateTime.AddMinutes(15), expires);
    }

    [Fact]
    public async Task Token_is_rejected_after_it_expires()
    {
        var (service, clock, options) = Create();
        var (token, _) = service.CreateAccessToken(User(), ["Driver"]);

        clock.Advance(TimeSpan.FromMinutes(16));
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, Parameters(options, Key, clock));

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Token_signed_with_a_different_key_is_rejected()
    {
        var (service, clock, options) = Create();
        var (token, _) = service.CreateAccessToken(User(), ["Driver"]);

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, Parameters(options, "a-completely-different-32-char-key-value!!", clock));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Refresh_tokens_are_long_unique_and_url_safe()
    {
        var (service, _, _) = Create();
        var tokens = Enumerable.Range(0, 50).Select(_ => service.GenerateRefreshToken()).ToList();

        Assert.Equal(50, tokens.Distinct().Count());
        Assert.All(tokens, t =>
        {
            Assert.True(t.Length >= 80);
            Assert.DoesNotContain('+', t);
            Assert.DoesNotContain('/', t);
            Assert.DoesNotContain('=', t);
        });
    }

    [Fact]
    public void Hash_is_deterministic_and_does_not_reveal_the_token()
    {
        var (service, _, _) = Create();
        var token = service.GenerateRefreshToken();

        Assert.Equal(service.Hash(token), service.Hash(token));
        Assert.NotEqual(service.Hash(token), service.Hash(token + "x"));
        Assert.DoesNotContain(token, service.Hash(token));
        Assert.Equal(64, service.Hash(token).Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("too-short")]
    public void Weak_signing_key_fails_options_validation(string key)
    {
        var provider = new ServiceCollection()
            .AddOptions<JwtOptions>().Configure(o => o.Key = key).ValidateDataAnnotations().Services
            .BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<JwtOptions>>().Value);
    }
}
