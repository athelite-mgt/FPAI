using FpaiConnect.Domain.Entities;
using FpaiConnect.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace FpaiConnect.Api.Security;

public record TokenPair(string AccessToken, string RefreshToken, DateTime AccessTokenExpiresAt);

public class JwtOptions
{
    public string Issuer { get; set; } = "FpaiConnect";
    public string Audience { get; set; } = "FpaiConnect.Client";
    public string SigningKey { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 60;
    public int RefreshTokenDays { get; set; } = 14;
}

public class JwtTokenService(
    Microsoft.Extensions.Options.IOptions<JwtOptions> options,
    UserManager<AppUser> users,
    AppDbContext db)
{
    private readonly JwtOptions _opt = options.Value;

    public async Task<TokenPair> IssueAsync(AppUser user, CancellationToken ct = default)
    {
        var roles = await users.GetRolesAsync(user);
        var expiresAt = DateTime.UtcNow.AddMinutes(_opt.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.UserName ?? user.Email ?? string.Empty),
            new(ClaimTypes.Email, user.Email ?? string.Empty),
            new(AppClaims.FullName, user.FullName)
        };
        if (user.DepartmentId is { } dept)
            claims.Add(new Claim(AppClaims.DepartmentId, dept.ToString()));
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opt.SigningKey));
        var token = new JwtSecurityToken(
            issuer: _opt.Issuer,
            audience: _opt.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiresAt,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        var accessToken = new JwtSecurityTokenHandler().WriteToken(token);
        var refreshToken = await IssueRefreshTokenAsync(user.Id, ct);

        return new TokenPair(accessToken, refreshToken, expiresAt);
    }

    private async Task<string> IssueRefreshTokenAsync(Guid userId, CancellationToken ct)
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = userId,
            TokenHash = Hash(raw),
            ExpiresAt = DateTime.UtcNow.AddDays(_opt.RefreshTokenDays)
        });
        await db.SaveChangesAsync(ct);
        return raw;
    }

    /// <summary>
    /// Rotates a refresh token: the presented token is revoked and a new pair issued.
    /// Returns null when the token is unknown, expired or already used.
    /// </summary>
    public async Task<(TokenPair Tokens, AppUser User)?> RefreshAsync(
        string presentedToken, CancellationToken ct = default)
    {
        var hash = Hash(presentedToken);
        var stored = await db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (stored is null || !stored.IsActive) return null;
        if (stored.User.IsDeleted || stored.User.Status != Domain.Enums.UserStatus.Active) return null;

        stored.RevokedAt = DateTime.UtcNow;
        var pair = await IssueAsync(stored.User, ct);
        stored.ReplacedByTokenHash = Hash(pair.RefreshToken);
        await db.SaveChangesAsync(ct);
        return (pair, stored.User);
    }

    public async Task RevokeAsync(string presentedToken, CancellationToken ct = default)
    {
        var hash = Hash(presentedToken);
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (stored is null || !stored.IsActive) return;
        stored.RevokedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Revokes every active refresh token for a user, e.g. on suspension or role change.</summary>
    public async Task RevokeAllForUserAsync(Guid userId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
