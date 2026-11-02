using Microsoft.AspNetCore.Identity;

namespace ParkWise.Infrastructure.Identity;

// Guid keys so the Domain can refer to a user as a plain Guid, with no Identity types leaking inward.
public class ApplicationUser : IdentityUser<Guid>
{
    public string FullName { get; set; } = string.Empty;

    /// <summary>Admin switch to block a user without deleting their history.</summary>
    public bool IsActive { get; set; } = true;
}

public class ApplicationRole : IdentityRole<Guid>
{
    public ApplicationRole() { }
    public ApplicationRole(string name) : base(name) { }
}

public class RefreshToken
{
    private RefreshToken() { }

    public RefreshToken(Guid userId, Guid familyId, string tokenHash, DateTime createdUtc, DateTime expiresUtc)
    {
        UserId = userId;
        FamilyId = familyId;
        TokenHash = tokenHash;
        CreatedUtc = createdUtc;
        ExpiresUtc = expiresUtc;
    }

    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid UserId { get; private set; }

    /// <summary>All tokens descended from one login share a family. Reuse of a revoked token kills the whole family.</summary>
    public Guid FamilyId { get; private set; }

    /// <summary>SHA-256 of the token. A database leak must not hand out working tokens.</summary>
    public string TokenHash { get; private set; } = null!;

    public DateTime CreatedUtc { get; private set; }
    public DateTime ExpiresUtc { get; private set; }
    public DateTime? RevokedUtc { get; private set; }
    public Guid? ReplacedByTokenId { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public bool IsRevoked => RevokedUtc is not null;
    public bool IsExpired(DateTime nowUtc) => nowUtc >= ExpiresUtc;

    public void Revoke(DateTime nowUtc, Guid? replacedBy = null)
    {
        RevokedUtc ??= nowUtc;
        ReplacedByTokenId ??= replacedBy;
    }
}
