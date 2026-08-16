using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SkyLIS.Application.Users;
using SkyLIS.Domain.Users;
using SkyLIS.Infrastructure.Persistence;

namespace SkyLIS.Infrastructure.Users;

/// <summary>PBKDF2-SHA256 (210k iterations, per-password salt). Format: iterations.salt.hash (base64).</summary>
internal sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const int Iterations = 210_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string hash)
    {
        var parts = hash.Split('.');
        if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations)) return false;
        var salt = Convert.FromBase64String(parts[1]);
        var expected = Convert.FromBase64String(parts[2]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}

internal sealed class UserConfig : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("users", "users");
        b.HasKey(u => u.Id);
        b.Property(u => u.TenantId).IsRequired();
        b.Property(u => u.UserName).HasMaxLength(60).IsRequired();
        b.HasIndex(u => new { u.TenantId, u.UserName }).IsUnique();
        b.Property(u => u.FullName).HasMaxLength(200).IsRequired();
        b.Property(u => u.PasswordHash).HasMaxLength(200).IsRequired();
        b.Property(u => u.Status).HasConversion<string>().HasMaxLength(20);
        // Roles persist as a comma-joined string from the private backing field (role
        // codes never contain commas); the read-only wrapper property is not mapped.
        b.Ignore(u => u.Roles);
        b.Property<List<string>>("_roles")
            .HasColumnName("roles")
            .HasMaxLength(500)
            .IsRequired()
            .HasConversion(
                roles => string.Join(',', roles),
                stored => stored.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
                new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<List<string>>(
                    (a, b) => a != null && b != null && a.SequenceEqual(b),
                    v => v.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode())),
                    v => v.ToList()));
        b.Property<uint>("xmin").IsRowVersion();
    }
}

internal sealed class UserRepository : IUserRepository
{
    private readonly SkyLisDbContext _db;
    public UserRepository(SkyLisDbContext db) => _db = db;

    public Task<User?> GetAsync(Guid id, CancellationToken ct = default) =>
        _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

    public Task<User?> FindByUserNameAsync(string userName, CancellationToken ct = default) =>
        _db.Users.FirstOrDefaultAsync(u => u.UserName == userName, ct);

    public Task<bool> UserNameExistsAsync(string userName, CancellationToken ct = default) =>
        _db.Users.AnyAsync(u => u.UserName == userName, ct);

    public Task<int> CountSeatsAsync(CancellationToken ct = default) =>
        _db.Users.CountAsync(u => u.Status != UserStatus.Deactivated, ct);

    public void Add(User user) => _db.Users.Add(user);
}

internal sealed class UserQueries : IUserQueries
{
    private readonly SkyLisDbContext _db;
    public UserQueries(SkyLisDbContext db) => _db = db;

    public async Task<IReadOnlyList<UserDto>> ListAsync(CancellationToken ct = default)
    {
        var users = await _db.Users.AsNoTracking()
            .OrderBy(u => u.UserName)
            .Take(200)
            .ToListAsync(ct);
        return users.Select(u => new UserDto(
            u.Id, u.UserName, u.FullName, u.Roles, u.Status.ToString(), u.LastLoginAtUtc)).ToList();
    }
}
