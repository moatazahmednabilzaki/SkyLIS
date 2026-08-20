using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SkyLIS.Application.Common;
using SkyLIS.Application.Users;
using SkyLIS.Domain.Platform;
using SkyLIS.Infrastructure.Persistence;

namespace SkyLIS.Infrastructure.Platform;

internal sealed class PlatformOperatorConfig : IEntityTypeConfiguration<PlatformOperator>
{
    public void Configure(EntityTypeBuilder<PlatformOperator> b)
    {
        b.ToTable("platform_operators", "platform");
        b.HasKey(o => o.Id);
        b.Property(o => o.UserName).HasMaxLength(60).IsRequired();
        b.HasIndex(o => o.UserName).IsUnique();
        b.Property(o => o.FullName).HasMaxLength(200).IsRequired();
        b.Property(o => o.PasswordHash).HasMaxLength(400).IsRequired();
        b.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
        b.Property<uint>("xmin").IsRowVersion();
    }
}

internal sealed class PlatformOperatorRepository : IPlatformOperatorRepository
{
    private readonly SkyLisDbContext _db;
    public PlatformOperatorRepository(SkyLisDbContext db) => _db = db;

    public Task<PlatformOperator?> GetAsync(Guid id, CancellationToken ct = default) =>
        _db.PlatformOperators.FirstOrDefaultAsync(o => o.Id == id, ct);

    public Task<PlatformOperator?> FindByUserNameAsync(string userName, CancellationToken ct = default) =>
        _db.PlatformOperators.FirstOrDefaultAsync(o => o.UserName == userName, ct);

    public Task<bool> AnyAsync(CancellationToken ct = default) =>
        _db.PlatformOperators.AnyAsync(ct);

    public void Add(PlatformOperator @operator) => _db.PlatformOperators.Add(@operator);
}

/// <summary>
/// Bootstraps the FIRST platform operator from configuration
/// (Platform:BootstrapOperator:{UserName,FullName,Password}) so a fresh on-prem install
/// has exactly one way in. Runs only when the operators table is empty — it can never
/// reset or duplicate an existing account, and it never logs the password.
/// </summary>
internal sealed class PlatformOperatorSeeder : IHostedService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PlatformOperatorSeeder> _logger;

    public PlatformOperatorSeeder(
        IServiceScopeFactory scopes, IConfiguration configuration, ILogger<PlatformOperatorSeeder> logger)
    {
        _scopes = scopes;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SkyLisDbContext>();
        if (await db.PlatformOperators.AnyAsync(ct))
            return;

        var userName = _configuration["Platform:BootstrapOperator:UserName"];
        var fullName = _configuration["Platform:BootstrapOperator:FullName"];
        var password = _configuration["Platform:BootstrapOperator:Password"];
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
        {
            _logger.LogWarning(
                "No platform operators exist and Platform:BootstrapOperator is not configured — "
                + "the Admin Portal cannot sign in until it is.");
            return;
        }

        // Fail fast on a weak bootstrap credential rather than seeding it: the first
        // operator is the master key to every tenant, so hold it to the same floor as a
        // tenant admin (ProvisionTenant requires 12+). Mirrors the signing-key guard.
        if (password.Trim().Length < 12)
            throw new InvalidOperationException(
                "Platform:BootstrapOperator:Password must be at least 12 characters. "
                + "Set a strong PLATFORM_ADMIN_PASSWORD before first boot.");

        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        db.PlatformOperators.Add(PlatformOperator.Create(
            Guid.CreateVersion7(), userName, fullName ?? userName, hasher.Hash(password), DateTimeOffset.UtcNow));
        await db.SaveChangesAsync(ct);
        _logger.LogInformation("Bootstrapped the first platform operator '{UserName}'.", userName.ToLowerInvariant());
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
