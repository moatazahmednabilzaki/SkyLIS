using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SkyLIS.Application.Common;
using SkyLIS.Infrastructure.Outbox;
using SkyLIS.Infrastructure.Persistence;

namespace SkyLIS.Infrastructure.Audit;

internal sealed class AuditEventConfig : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> b)
    {
        b.ToTable("audit_events", "audit");
        b.HasKey(a => a.Id);
        b.HasIndex(a => new { a.TenantId, a.OccurredAtUtc });
        b.HasIndex(a => new { a.TenantId, a.EntityType, a.EntityId });
        b.Property(a => a.Action).HasMaxLength(10).IsRequired();
        b.Property(a => a.EntityType).HasMaxLength(120).IsRequired();
        b.Property(a => a.EntityId).HasMaxLength(60).IsRequired();
        // Deliberately TEXT, not jsonb: jsonb normalizes formatting on storage, which
        // would break the hash chain's exact byte round-trip. Query-side JSON access can
        // cast (new_values::jsonb) when needed.
        b.Property(a => a.OldValues);
        b.Property(a => a.NewValues);
        b.Property(a => a.IpAddress).HasMaxLength(45);
        b.Property(a => a.PreviousHash).HasMaxLength(64).IsRequired();
        b.Property(a => a.Hash).HasMaxLength(64).IsRequired();
    }
}

/// <summary>
/// Builds audit events from the EF change tracker. Excluded: the audit table itself,
/// the outbox (its own evidentiary record), number-series counters (noise), and
/// oversized artifact payloads (hash-protected elsewhere).
/// </summary>
internal static class AuditCollector
{
    private static readonly HashSet<Type> ExcludedTypes =
        [typeof(AuditEvent), typeof(OutboxMessage), typeof(InboxConsumption), typeof(NumberSeries)];

    private static readonly HashSet<string> OmittedProperties = ["ContentHtml"];

    public static List<AuditEvent> Collect(
        ChangeTracker changeTracker, Guid? tenantId, Guid? userId, string? ipAddress, DateTimeOffset nowUtc)
    {
        // PostgreSQL stores timestamps at microsecond precision; .NET ticks are 100 ns.
        // Truncate BEFORE hashing so the persisted value round-trips byte-identical.
        nowUtc = new DateTimeOffset(nowUtc.Ticks - (nowUtc.Ticks % 10), TimeSpan.Zero);
        var events = new List<AuditEvent>();
        foreach (var entry in changeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                continue;
            var clrType = entry.Metadata.ClrType;
            if (ExcludedTypes.Contains(clrType)) continue;

            var (oldValues, newValues) = SnapshotValues(entry);
            if (entry.State == EntityState.Modified && newValues is null)
                continue; // nothing actually changed

            events.Add(new AuditEvent
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenantId,
                UserId = userId,
                Action = entry.State.ToString() switch
                {
                    "Added" => "Created",
                    "Deleted" => "Deleted",
                    _ => "Modified",
                },
                EntityType = entry.Metadata.ShortName(),
                EntityId = PrimaryKeyOf(entry),
                OldValues = oldValues,
                NewValues = newValues,
                IpAddress = ipAddress,
                OccurredAtUtc = nowUtc,
            });
        }
        return events;
    }

    private static string PrimaryKeyOf(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();
        if (key is null) return "(keyless)";
        return string.Join("|", key.Properties.Select(p => entry.Property(p.Name).CurrentValue?.ToString() ?? "null"));
    }

    private static (string? Old, string? New) SnapshotValues(EntityEntry entry)
    {
        var oldValues = new Dictionary<string, object?>();
        var newValues = new Dictionary<string, object?>();

        foreach (var property in entry.Properties)
        {
            var name = property.Metadata.Name;
            if (property.Metadata.IsShadowProperty()) continue;
            object? Shape(object? value) =>
                OmittedProperties.Contains(name) && value is string s
                    ? $"(omitted: {s.Length} chars, hash-protected)"
                    : value;

            switch (entry.State)
            {
                case EntityState.Added:
                    newValues[name] = Shape(property.CurrentValue);
                    break;
                case EntityState.Deleted:
                    oldValues[name] = Shape(property.OriginalValue);
                    break;
                case EntityState.Modified when property.IsModified:
                    oldValues[name] = Shape(property.OriginalValue);
                    newValues[name] = Shape(property.CurrentValue);
                    break;
            }
        }

        return (
            oldValues.Count > 0 ? JsonSerializer.Serialize(oldValues) : null,
            newValues.Count > 0 ? JsonSerializer.Serialize(newValues) : null);
    }
}
