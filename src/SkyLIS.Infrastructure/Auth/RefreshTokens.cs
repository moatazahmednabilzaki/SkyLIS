using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Configuration;
using SkyLIS.Application.Common;
using SkyLIS.Infrastructure.Persistence;

namespace SkyLIS.Infrastructure.Auth;

/// <summary>
/// One issued refresh token (platform schema — sessions exist for tenant users AND
/// platform operators). Only the SHA-256 hash is stored; the raw token leaves the server
/// exactly once. Rotation links the replacement for audit forensics.
/// </summary>
public sealed class RefreshToken
{
    public Guid Id { get; set; }
    public string TokenHash { get; set; } = null!;
    public Guid PrincipalId { get; set; }
    public string PrincipalType { get; set; } = null!;
    public Guid? TenantId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
    public Guid? ReplacedById { get; set; }
}

internal sealed class RefreshTokenConfig : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.ToTable("refresh_tokens", "platform");
        b.HasKey(t => t.Id);
        b.Property(t => t.TokenHash).HasMaxLength(64).IsRequired();
        b.HasIndex(t => t.TokenHash).IsUnique();
        b.Property(t => t.PrincipalType).HasMaxLength(20).IsRequired();
        b.HasIndex(t => new { t.PrincipalId, t.RevokedAtUtc });
    }
}

internal sealed class RefreshTokenStore : IRefreshTokenStore
{
    private readonly SkyLisDbContext _db;
    private readonly IClock _clock;
    private readonly TimeSpan _lifetime;

    public RefreshTokenStore(SkyLisDbContext db, IClock clock, IConfiguration configuration)
    {
        _db = db;
        _clock = clock;
        _lifetime = TimeSpan.FromHours(
            double.TryParse(configuration["Auth:RefreshTokenHours"], out var hours) ? hours : 12);
    }

    public Task<string> IssueAsync(Guid principalId, string principalType, Guid? tenantId, CancellationToken ct = default)
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        _db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.CreateVersion7(),
            TokenHash = Hash(raw),
            PrincipalId = principalId,
            PrincipalType = principalType,
            TenantId = tenantId,
            CreatedAtUtc = _clock.UtcNow,
            ExpiresAtUtc = _clock.UtcNow.Add(_lifetime),
        });
        return Task.FromResult(raw);
    }

    public async Task<RefreshTokenInfo?> ValidateAsync(string rawToken, CancellationToken ct = default)
    {
        var hash = Hash(rawToken);
        var token = await _db.RefreshTokens.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (token is null || token.RevokedAtUtc is not null || token.ExpiresAtUtc <= _clock.UtcNow)
            return null;
        return new RefreshTokenInfo(token.Id, token.PrincipalId, token.PrincipalType, token.TenantId);
    }

    public async Task<string> RotateAsync(
        Guid currentTokenId, Guid principalId, string principalType, Guid? tenantId, CancellationToken ct = default)
    {
        var current = await _db.RefreshTokens.FirstAsync(t => t.Id == currentTokenId, ct);
        var raw = await IssueAsync(principalId, principalType, tenantId, ct);
        current.RevokedAtUtc = _clock.UtcNow;
        current.ReplacedById = _db.ChangeTracker.Entries<RefreshToken>()
            .Single(e => e.State == Microsoft.EntityFrameworkCore.EntityState.Added).Entity.Id;
        return raw;
    }

    public async Task RevokeAsync(string rawToken, CancellationToken ct = default)
    {
        var hash = Hash(rawToken);
        var token = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (token is not null && token.RevokedAtUtc is null)
            token.RevokedAtUtc = _clock.UtcNow;
    }

    public async Task RevokeAllForPrincipalAsync(Guid principalId, CancellationToken ct = default)
    {
        var live = await _db.RefreshTokens
            .Where(t => t.PrincipalId == principalId && t.RevokedAtUtc == null)
            .ToListAsync(ct);
        foreach (var token in live)
            token.RevokedAtUtc = _clock.UtcNow;
    }

    private static string Hash(string raw) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw)));
}
