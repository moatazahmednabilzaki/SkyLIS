using FluentValidation;
using MediatR;
using SkyLIS.Application.Common;
using SkyLIS.Domain.Platform;

namespace SkyLIS.Application.Platform;

public sealed record MasterTestDto(
    Guid Id, string Code, string Name, string Department, string SampleTypeName,
    string ContainerName, string? ConditionName, DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastPushedAtUtc, int PushCount);

public interface IMasterTestQueries
{
    Task<IReadOnlyList<MasterTestDto>> ListAsync(CancellationToken ct = default);
}

public sealed record ListMasterTestsQuery : IQuery<IReadOnlyList<MasterTestDto>>, IPlatformScoped, IRequirePermission
{
    public string Permission => "platform.masterdata.read";
}

internal sealed class ListMasterTestsHandler : IRequestHandler<ListMasterTestsQuery, IReadOnlyList<MasterTestDto>>
{
    private readonly IMasterTestQueries _queries;
    public ListMasterTestsHandler(IMasterTestQueries queries) => _queries = queries;

    public Task<IReadOnlyList<MasterTestDto>> Handle(ListMasterTestsQuery request, CancellationToken ct) =>
        _queries.ListAsync(ct);
}

/// <summary>P01.7: add a test to the platform master catalogue.</summary>
public sealed record CreateMasterTestCommand(
    string Code, string Name, string Department,
    string SampleTypeName, string ContainerName, string? ConditionName)
    : ICommand<Guid>, IPlatformScoped, IRequirePermission
{
    public string Permission => "platform.masterdata.manage";
}

internal sealed class CreateMasterTestValidator : AbstractValidator<CreateMasterTestCommand>
{
    public CreateMasterTestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Department).NotEmpty().MaximumLength(80);
        RuleFor(x => x.SampleTypeName).NotEmpty().MaximumLength(80);
        RuleFor(x => x.ContainerName).NotEmpty().MaximumLength(80);
        RuleFor(x => x.ConditionName).MaximumLength(80);
    }
}

internal sealed class CreateMasterTestHandler : IRequestHandler<CreateMasterTestCommand, Guid>
{
    private readonly IMasterTestRepository _masterTests;
    private readonly IClock _clock;

    public CreateMasterTestHandler(IMasterTestRepository masterTests, IClock clock)
    {
        _masterTests = masterTests;
        _clock = clock;
    }

    public async Task<Guid> Handle(CreateMasterTestCommand request, CancellationToken ct)
    {
        var code = request.Code.Trim().ToUpperInvariant();
        if (await _masterTests.CodeExistsAsync(code, ct))
            throw new ConflictException($"Master test '{code}' already exists.");

        var masterTest = MasterTest.Create(
            Guid.CreateVersion7(), code, request.Name, request.Department,
            request.SampleTypeName, request.ContainerName, request.ConditionName, _clock.UtcNow);
        _masterTests.Add(masterTest);
        return masterTest.Id;
    }
}

/// <summary>
/// FR-MDM-071: push a master test to every eligible tenant. One reliable outbox event per
/// tenant; each consumer creates the test there as PendingActivation (price gate stays local).
/// </summary>
public sealed record PushMasterTestCommand(Guid MasterTestId) : ICommand<int>, IPlatformScoped, IRequirePermission
{
    public string Permission => "platform.masterdata.manage";
}

internal sealed class PushMasterTestHandler : IRequestHandler<PushMasterTestCommand, int>
{
    private readonly IMasterTestRepository _masterTests;
    private readonly ITenantRepository _tenants;
    private readonly IClock _clock;

    public PushMasterTestHandler(IMasterTestRepository masterTests, ITenantRepository tenants, IClock clock)
    {
        _masterTests = masterTests;
        _tenants = tenants;
        _clock = clock;
    }

    public async Task<int> Handle(PushMasterTestCommand request, CancellationToken ct)
    {
        var masterTest = await _masterTests.GetAsync(request.MasterTestId, ct)
            ?? throw new NotFoundException("MasterTest", request.MasterTestId);
        var targets = await _tenants.GetPushTargetTenantIdsAsync(ct);
        masterTest.PushTo(targets, _clock.UtcNow);
        return targets.Count;
    }
}
