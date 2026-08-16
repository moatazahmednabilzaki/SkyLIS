using FluentValidation;
using MediatR;
using SkyLIS.Application.Common;
using SkyLIS.Domain.Common;
using SkyLIS.Domain.Patients;

namespace SkyLIS.Application.Patients;

// ---------- P04.4: duplicate detection & merge ----------

public sealed record DuplicateCandidateDto(
    Guid Id, string PatientNumber, string FullName, string Mobile, DateOnly DateOfBirth,
    DateTimeOffset? LastVisitAtUtc, int VisitCount);

public sealed record DuplicateGroupDto(string MatchedOn, IReadOnlyList<DuplicateCandidateDto> Patients);

/// <summary>Set-based re-pointing of clinical artifacts during a merge (Infrastructure).</summary>
public interface IPatientMergeStore
{
    Task<int> RepointAsync(Guid duplicatePatientId, Guid survivorPatientId, CancellationToken ct = default);
}

public interface IPatientPrivacyQueries
{
    Task<IReadOnlyList<DuplicateGroupDto>> FindDuplicatesAsync(CancellationToken ct = default);
    Task<object?> ExportAsync(Guid patientId, CancellationToken ct = default);
    Task<IReadOnlyList<DataSubjectRequestDto>> ListRequestsAsync(CancellationToken ct = default);
}

public sealed record FindDuplicatesQuery : IQuery<IReadOnlyList<DuplicateGroupDto>>, IRequirePermission
{
    public string Permission => "patients.patient.read";
}

internal sealed class FindDuplicatesHandler : IRequestHandler<FindDuplicatesQuery, IReadOnlyList<DuplicateGroupDto>>
{
    private readonly IPatientPrivacyQueries _queries;
    public FindDuplicatesHandler(IPatientPrivacyQueries queries) => _queries = queries;
    public Task<IReadOnlyList<DuplicateGroupDto>> Handle(FindDuplicatesQuery request, CancellationToken ct) =>
        _queries.FindDuplicatesAsync(ct);
}

/// <summary>
/// P04.4: merge a duplicate into the survivor. Clinical artifacts (visits, results,
/// reports) are re-pointed set-based in the same request; the duplicate row remains,
/// marked and hidden from search — nothing is deleted.
/// </summary>
public sealed record MergePatientsCommand(Guid SurvivorId, Guid DuplicateId, string Reason)
    : ICommand<int>, IRequirePermission
{
    public string Permission => "patients.patient.merge";
}

internal sealed class MergePatientsValidator : AbstractValidator<MergePatientsCommand>
{
    public MergePatientsValidator()
    {
        RuleFor(x => x.SurvivorId).NotEmpty();
        RuleFor(x => x.DuplicateId).NotEmpty().NotEqual(x => x.SurvivorId)
            .WithMessage("A patient cannot be merged into itself.");
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(300);
    }
}

internal sealed class MergePatientsHandler : IRequestHandler<MergePatientsCommand, int>
{
    private readonly IPatientRepository _patients;
    private readonly IPatientMergeStore _mergeStore;

    public MergePatientsHandler(IPatientRepository patients, IPatientMergeStore mergeStore)
    {
        _patients = patients;
        _mergeStore = mergeStore;
    }

    public async Task<int> Handle(MergePatientsCommand request, CancellationToken ct)
    {
        var survivor = await _patients.GetAsync(request.SurvivorId, ct)
            ?? throw new NotFoundException("Patient (survivor)", request.SurvivorId);
        var duplicate = await _patients.GetAsync(request.DuplicateId, ct)
            ?? throw new NotFoundException("Patient (duplicate)", request.DuplicateId);
        if (survivor.MergedIntoPatientId is not null)
            throw new DomainException("The survivor was itself merged — merge into the final survivor instead.");

        duplicate.MarkMergedInto(survivor.Id);
        if (duplicate.LastVisitAtUtc is not null
            && (survivor.LastVisitAtUtc is null || duplicate.LastVisitAtUtc > survivor.LastVisitAtUtc))
        {
            survivor.RecordVisit(duplicate.LastVisitAtUtc.Value);
        }

        return await _mergeStore.RepointAsync(duplicate.Id, survivor.Id, ct);
    }
}

// ---------- P04.5: data-subject requests ----------

public sealed record DataSubjectRequestDto(
    Guid Id, Guid PatientId, string PatientNumber, string Kind, string Status,
    string Reason, DateTimeOffset CreatedAtUtc, DateTimeOffset? DecidedAtUtc);

public interface IDataSubjectRequestRepository
{
    Task<DataSubjectRequest?> GetAsync(Guid id, CancellationToken ct = default);
    void Add(DataSubjectRequest request);
}

public sealed record ListDataSubjectRequestsQuery : IQuery<IReadOnlyList<DataSubjectRequestDto>>, IRequirePermission
{
    public string Permission => "patients.patient.read";
}

internal sealed class ListDataSubjectRequestsHandler
    : IRequestHandler<ListDataSubjectRequestsQuery, IReadOnlyList<DataSubjectRequestDto>>
{
    private readonly IPatientPrivacyQueries _queries;
    public ListDataSubjectRequestsHandler(IPatientPrivacyQueries queries) => _queries = queries;
    public Task<IReadOnlyList<DataSubjectRequestDto>> Handle(ListDataSubjectRequestsQuery request, CancellationToken ct) =>
        _queries.ListRequestsAsync(ct);
}

/// <summary>P04.5 export: returns the patient's data bundle and leaves an audited request record.</summary>
public sealed record ExportPatientDataCommand(Guid PatientId, string Reason) : ICommand<object>, IRequirePermission
{
    public string Permission => "patients.patient.read";
}

internal sealed class ExportPatientDataValidator : AbstractValidator<ExportPatientDataCommand>
{
    public ExportPatientDataValidator()
    {
        RuleFor(x => x.PatientId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(300);
    }
}

internal sealed class ExportPatientDataHandler : IRequestHandler<ExportPatientDataCommand, object>
{
    private readonly IPatientPrivacyQueries _queries;
    private readonly IDataSubjectRequestRepository _requests;
    private readonly ITenantContext _tenant;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;

    public ExportPatientDataHandler(
        IPatientPrivacyQueries queries, IDataSubjectRequestRepository requests,
        ITenantContext tenant, ICurrentUser user, IClock clock)
    {
        _queries = queries;
        _requests = requests;
        _tenant = tenant;
        _user = user;
        _clock = clock;
    }

    public async Task<object> Handle(ExportPatientDataCommand request, CancellationToken ct)
    {
        var bundle = await _queries.ExportAsync(request.PatientId, ct)
            ?? throw new NotFoundException("Patient", request.PatientId);
        _requests.Add(DataSubjectRequest.Create(
            Guid.CreateVersion7(), _tenant.TenantId, request.PatientId,
            DataSubjectRequestKind.Export, request.Reason, _user.UserId, _clock.UtcNow));
        return bundle;
    }
}

public sealed record RequestErasureCommand(Guid PatientId, string Reason) : ICommand<Guid>, IRequirePermission
{
    public string Permission => "patients.patient.create";
}

internal sealed class RequestErasureValidator : AbstractValidator<RequestErasureCommand>
{
    public RequestErasureValidator()
    {
        RuleFor(x => x.PatientId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(300);
    }
}

internal sealed class RequestErasureHandler : IRequestHandler<RequestErasureCommand, Guid>
{
    private readonly IPatientRepository _patients;
    private readonly IDataSubjectRequestRepository _requests;
    private readonly ITenantContext _tenant;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;

    public RequestErasureHandler(
        IPatientRepository patients, IDataSubjectRequestRepository requests,
        ITenantContext tenant, ICurrentUser user, IClock clock)
    {
        _patients = patients;
        _requests = requests;
        _tenant = tenant;
        _user = user;
        _clock = clock;
    }

    public async Task<Guid> Handle(RequestErasureCommand request, CancellationToken ct)
    {
        _ = await _patients.GetAsync(request.PatientId, ct)
            ?? throw new NotFoundException("Patient", request.PatientId);
        var dsr = DataSubjectRequest.Create(
            Guid.CreateVersion7(), _tenant.TenantId, request.PatientId,
            DataSubjectRequestKind.Erasure, request.Reason, _user.UserId, _clock.UtcNow);
        _requests.Add(dsr);
        return dsr.Id;
    }
}

/// <summary>
/// P04.5 erasure approval (SoD): anonymizes identity while clinical records are retained
/// under laboratory record-keeping obligations. Blocked while clinical work is open.
/// </summary>
public sealed record ApproveErasureCommand(Guid RequestId) : ICommand<Unit>, IRequirePermission
{
    public string Permission => "patients.patient.erase";
}

internal sealed class ApproveErasureHandler : IRequestHandler<ApproveErasureCommand, Unit>
{
    private readonly IDataSubjectRequestRepository _requests;
    private readonly IPatientRepository _patients;
    private readonly IPatientPrivacyQueries _queries;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;

    public ApproveErasureHandler(
        IDataSubjectRequestRepository requests, IPatientRepository patients,
        IPatientPrivacyQueries queries, ICurrentUser user, IClock clock)
    {
        _requests = requests;
        _patients = patients;
        _queries = queries;
        _user = user;
        _clock = clock;
    }

    public async Task<Unit> Handle(ApproveErasureCommand request, CancellationToken ct)
    {
        var dsr = await _requests.GetAsync(request.RequestId, ct)
            ?? throw new NotFoundException("DataSubjectRequest", request.RequestId);
        var patient = await _patients.GetAsync(dsr.PatientId, ct)
            ?? throw new NotFoundException("Patient", dsr.PatientId);
        if (await _patients.HasOpenClinicalWorkAsync(patient.Id, ct))
            throw new DomainException(
                "Erasure is blocked while the patient has open clinical work (unreported visits).");

        dsr.Approve(_user.UserId, _clock.UtcNow);
        patient.Anonymize();
        return Unit.Value;
    }
}
