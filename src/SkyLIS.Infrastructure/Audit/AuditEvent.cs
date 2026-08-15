using System.Security.Cryptography;
using System.Text;

namespace SkyLIS.Infrastructure.Audit;

/// <summary>
/// FR-SYS-001: one append-only audit event. Written in the SAME transaction as the
/// business change (NFR-007) and hash-chained per tenant: each event's hash covers its
/// payload plus the previous event's hash, so any retroactive edit breaks every later
/// link. Production hardening (revoking UPDATE/DELETE from the app role and monthly
/// partitioning) is applied by the enable-rls.sql companion notes.
/// </summary>
public sealed class AuditEvent
{
    public Guid Id { get; init; }
    /// <summary>Null for platform-scoped operations (their own chain).</summary>
    public Guid? TenantId { get; init; }
    public Guid? UserId { get; init; }
    public string Action { get; init; } = null!;      // Created | Modified | Deleted
    public string EntityType { get; init; } = null!;
    public string EntityId { get; init; } = null!;
    public string? OldValues { get; init; }
    public string? NewValues { get; init; }
    public string? IpAddress { get; init; }
    public DateTimeOffset OccurredAtUtc { get; init; }
    public string PreviousHash { get; set; } = null!;
    public string Hash { get; set; } = null!;

    public const string GenesisHash = "GENESIS";

    public string ComputeHash(string previousHash) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{previousHash}|{TenantId}|{UserId}|{Action}|{EntityType}|{EntityId}|{OccurredAtUtc:o}|{OldValues}|{NewValues}")));
}
