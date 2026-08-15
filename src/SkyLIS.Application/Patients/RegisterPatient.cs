using FluentValidation;
using MediatR;
using SkyLIS.Application.Common;
using SkyLIS.Domain.Common;
using SkyLIS.Domain.Patients;

namespace SkyLIS.Application.Patients;

/// <summary>
/// FR-PAT-010: create the patient master record behind visit registration.
/// Data is captured once and auto-saved with a unique patient number.
/// </summary>
public sealed record RegisterPatientCommand(
    string FullName,
    Sex Sex,
    DateOnly DateOfBirth,
    string Mobile,
    string? NationalId) : ICommand<Guid>, IRequirePermission
{
    public string Permission => "patients.patient.create";
}

internal sealed class RegisterPatientValidator : AbstractValidator<RegisterPatientCommand>
{
    public RegisterPatientValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MinimumLength(3).MaximumLength(200);
        RuleFor(x => x.Mobile).NotEmpty().MaximumLength(20);
        RuleFor(x => x.NationalId).MaximumLength(30);
        RuleFor(x => x.DateOfBirth).NotEmpty();
    }
}

internal sealed class RegisterPatientHandler : IRequestHandler<RegisterPatientCommand, Guid>
{
    private readonly IPatientRepository _patients;
    private readonly ITenantContext _tenant;
    private readonly INumberSeriesService _numbers;
    private readonly IClock _clock;

    public RegisterPatientHandler(
        IPatientRepository patients, ITenantContext tenant, INumberSeriesService numbers, IClock clock)
    {
        _patients = patients;
        _tenant = tenant;
        _numbers = numbers;
        _clock = clock;
    }

    public async Task<Guid> Handle(RegisterPatientCommand request, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(request.NationalId) &&
            await _patients.NationalIdExistsAsync(request.NationalId.Trim(), ct))
        {
            throw new ConflictException("A patient with this national ID already exists.");
        }

        var patientNumber = await _numbers.NextAsync("patient", ct);
        var patient = Patient.Register(
            Guid.CreateVersion7(), _tenant.TenantId, patientNumber, request.FullName,
            request.Sex, request.DateOfBirth, PhoneNumber.Of(request.Mobile),
            request.NationalId, _clock.UtcNow);

        _patients.Add(patient);
        return patient.Id;
    }
}
