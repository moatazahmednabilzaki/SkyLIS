using FluentValidation;
using MediatR;
using SkyLIS.Application.Common;
using SkyLIS.Domain.Platform;

namespace SkyLIS.Application.Platform;

public sealed record PackConditionDto(string Name, int? DelayMinutes, string CompatibilityGroup);
public sealed record PackSampleTypeDto(string Name, string ContainerName, IReadOnlyList<PackConditionDto> Conditions);
public sealed record CountryPackDto(
    Guid Id, string CountryCode, string Name, string Currency, int Version,
    DateTimeOffset UpdatedAtUtc, IReadOnlyList<PackSampleTypeDto> SampleTypes);

/// <summary>Read side for the Admin Portal country packs page (P01.4).</summary>
public interface ICountryPackQueries
{
    Task<IReadOnlyList<CountryPackDto>> ListAsync(CancellationToken ct = default);
}

public sealed record ListCountryPacksQuery : IQuery<IReadOnlyList<CountryPackDto>>, IPlatformScoped, IRequirePermission
{
    public string Permission => "platform.masterdata.read";
}

internal sealed class ListCountryPacksHandler : IRequestHandler<ListCountryPacksQuery, IReadOnlyList<CountryPackDto>>
{
    private readonly ICountryPackQueries _queries;
    public ListCountryPacksHandler(ICountryPackQueries queries) => _queries = queries;

    public Task<IReadOnlyList<CountryPackDto>> Handle(ListCountryPacksQuery request, CancellationToken ct) =>
        _queries.ListAsync(ct);
}

/// <summary>
/// P01.4: create or replace a country default pack. Existing tenants are untouched —
/// packs apply at provisioning time only (FR-TEN-040).
/// </summary>
public sealed record UpsertCountryPackCommand(
    string CountryCode, string Name, string Currency,
    IReadOnlyList<PackSampleTypeDto> SampleTypes) : ICommand<Guid>, IPlatformScoped, IRequirePermission
{
    public string Permission => "platform.masterdata.manage";
}

internal sealed class UpsertCountryPackValidator : AbstractValidator<UpsertCountryPackCommand>
{
    public UpsertCountryPackValidator()
    {
        RuleFor(x => x.CountryCode).NotEmpty().Length(2);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Currency).NotEmpty().Length(3);
        RuleFor(x => x.SampleTypes).NotEmpty();
        RuleForEach(x => x.SampleTypes).ChildRules(sampleType =>
        {
            sampleType.RuleFor(s => s.Name).NotEmpty().MaximumLength(80);
            sampleType.RuleFor(s => s.ContainerName).NotEmpty().MaximumLength(80);
            sampleType.RuleForEach(s => s.Conditions).ChildRules(condition =>
            {
                condition.RuleFor(c => c.Name).NotEmpty().MaximumLength(80);
                condition.RuleFor(c => c.CompatibilityGroup).NotEmpty().MaximumLength(40);
                condition.RuleFor(c => c.DelayMinutes).InclusiveBetween(0, 1440).When(c => c.DelayMinutes.HasValue);
            });
        });
    }
}

internal sealed class UpsertCountryPackHandler : IRequestHandler<UpsertCountryPackCommand, Guid>
{
    private readonly ICountryPackRepository _packs;
    private readonly IClock _clock;

    public UpsertCountryPackHandler(ICountryPackRepository packs, IClock clock)
    {
        _packs = packs;
        _clock = clock;
    }

    public async Task<Guid> Handle(UpsertCountryPackCommand request, CancellationToken ct)
    {
        var content = request.SampleTypes
            .Select(s => new PackSampleType(s.Name, s.ContainerName,
                s.Conditions.Select(c => new PackCondition(c.Name, c.DelayMinutes, c.CompatibilityGroup)).ToList()))
            .ToList();

        var existing = await _packs.GetByCountryAsync(request.CountryCode.Trim().ToUpperInvariant(), ct);
        if (existing is not null)
        {
            existing.ReplaceContent(request.Name, request.Currency, content, _clock.UtcNow);
            return existing.Id;
        }

        var pack = CountryPack.Create(
            Guid.CreateVersion7(), request.CountryCode, request.Name, request.Currency, content, _clock.UtcNow);
        _packs.Add(pack);
        return pack.Id;
    }
}
