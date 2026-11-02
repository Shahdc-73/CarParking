using System.ComponentModel.DataAnnotations;

namespace ParkWise.Infrastructure.Identity;

public sealed class JwtOptions
{
    public const string Section = "Jwt";

    [Required, MinLength(32, ErrorMessage = "Jwt:Key must be at least 32 characters (256 bits) for HS256.")]
    public string Key { get; set; } = string.Empty;

    [Required] public string Issuer { get; set; } = "ParkWise";
    [Required] public string Audience { get; set; } = "ParkWise.Web";

    [Range(1, 1440)] public int AccessTokenMinutes { get; set; } = 15;
    [Range(1, 90)] public int RefreshTokenDays { get; set; } = 7;
}
