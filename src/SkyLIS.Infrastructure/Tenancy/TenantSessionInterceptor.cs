using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace SkyLIS.Infrastructure.Tenancy;

/// <summary>
/// Injects the ambient tenant into the PostgreSQL session on every logical connection
/// open, so Row-Level Security policies (enable-rls.sql) see app.tenant_id. Pooled
/// physical connections are re-set on each open, preventing tenant leakage between
/// requests. Platform-scoped requests (no tenant) set the all-zero sentinel, which can
/// never match a tenant_id, so tenant-owned rows stay invisible.
/// </summary>
public sealed class TenantSessionInterceptor : DbConnectionInterceptor
{
    private const string NoTenant = "00000000-0000-0000-0000-000000000000";
    private readonly TenantContext _tenantContext;

    public TenantSessionInterceptor(TenantContext tenantContext) => _tenantContext = tenantContext;

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
        => SetTenant(connection);

    public override async Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken ct = default)
    {
        await SetTenantAsync(connection, ct);
    }

    private void SetTenant(DbConnection connection)
    {
        using var command = BuildCommand(connection);
        command.ExecuteNonQuery();
    }

    private async Task SetTenantAsync(DbConnection connection, CancellationToken ct)
    {
        await using var command = BuildCommand(connection);
        await command.ExecuteNonQueryAsync(ct);
    }

    private DbCommand BuildCommand(DbConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = "SELECT set_config('app.tenant_id', $1, false)";
        var parameter = command.CreateParameter();
        parameter.Value = _tenantContext.HasTenant ? _tenantContext.TenantId.ToString() : NoTenant;
        command.Parameters.Add(parameter);
        return command;
    }
}
