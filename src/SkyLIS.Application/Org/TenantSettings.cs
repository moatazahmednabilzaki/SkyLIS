using FluentValidation;
using MediatR;
using SkyLIS.Application.Common;
using SkyLIS.Domain.Org;

namespace SkyLIS.Application.Org;

public sealed record TenantSettingDto(string Key, string Value, DateTimeOffset UpdatedAtUtc);

public interface ITenantSettingRepository
{
    Task<TenantSetting?> GetByKeyAsync(string key, CancellationToken ct = default);
    void Add(TenantSetting setting);
}

public interface ITenantSettingQueries
{
    Task<IReadOnlyList<TenantSettingDto>> ListAsync(CancellationToken ct = default);
    Task<string?> GetValueAsync(string key, CancellationToken ct = default);
}

public sealed record ListTenantSettingsQuery : IQuery<IReadOnlyList<TenantSettingDto>>, IRequirePermission
{
    public string Permission => "org.branch.read";
}

internal sealed class ListTenantSettingsHandler
    : IRequestHandler<ListTenantSettingsQuery, IReadOnlyList<TenantSettingDto>>
{
    private readonly ITenantSettingQueries _queries;
    public ListTenantSettingsHandler(ITenantSettingQueries queries) => _queries = queries;
    public Task<IReadOnlyList<TenantSettingDto>> Handle(ListTenantSettingsQuery request, CancellationToken ct) =>
        _queries.ListAsync(ct);
}

/// <summary>FR-SYS-004: set (upsert) one tenant configuration value.</summary>
public sealed record SetTenantSettingCommand(string Key, string Value) : ICommand<Unit>, IRequirePermission
{
    public string Permission => "org.branch.manage";
}

internal sealed class SetTenantSettingValidator : AbstractValidator<SetTenantSettingCommand>
{
    public SetTenantSettingValidator()
    {
        RuleFor(x => x.Key).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Value).NotEmpty().MaximumLength(2000);
    }
}

internal sealed class SetTenantSettingHandler : IRequestHandler<SetTenantSettingCommand, Unit>
{
    private readonly ITenantSettingRepository _settings;
    private readonly ITenantContext _tenant;
    private readonly IClock _clock;

    public SetTenantSettingHandler(ITenantSettingRepository settings, ITenantContext tenant, IClock clock)
    {
        _settings = settings;
        _tenant = tenant;
        _clock = clock;
    }

    public async Task<Unit> Handle(SetTenantSettingCommand request, CancellationToken ct)
    {
        var key = request.Key.Trim().ToLowerInvariant();
        var existing = await _settings.GetByKeyAsync(key, ct);
        if (existing is not null)
            existing.Update(request.Value, _clock.UtcNow);
        else
            _settings.Add(TenantSetting.Create(
                Guid.CreateVersion7(), _tenant.TenantId, key, request.Value, _clock.UtcNow));
        return Unit.Value;
    }
}

// ---------- P03.1: setup wizard status ----------

public sealed record SetupStatusDto(
    int Branches, int Departments, int SampleTypes, int ActiveTests, int Panels, int Users, int Settings,
    bool CatalogReady, bool TeamReady);

public interface ISetupStatusQueries
{
    Task<SetupStatusDto> StatusAsync(CancellationToken ct = default);
}

/// <summary>P03.1: the guided setup checklist — where does this lab stand?</summary>
public sealed record GetSetupStatusQuery : IQuery<SetupStatusDto>, IRequirePermission
{
    public string Permission => "org.branch.read";
}

internal sealed class GetSetupStatusHandler : IRequestHandler<GetSetupStatusQuery, SetupStatusDto>
{
    private readonly ISetupStatusQueries _queries;
    public GetSetupStatusHandler(ISetupStatusQueries queries) => _queries = queries;
    public Task<SetupStatusDto> Handle(GetSetupStatusQuery request, CancellationToken ct) =>
        _queries.StatusAsync(ct);
}
