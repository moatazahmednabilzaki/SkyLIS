using FluentAssertions;
using SkyLIS.Application.Common;
using SkyLIS.Application.Visits;
using SkyLIS.Domain.Billing;
using SkyLIS.Domain.Catalog;
using SkyLIS.Domain.Common;
using SkyLIS.Domain.Patients;
using SkyLIS.Domain.Visits;
using Xunit;

namespace SkyLIS.Application.Tests;

public class RegisterVisitHandlerTests
{
    private readonly FakeClock _clock = new();
    private readonly FakeTenantContext _tenant = new();
    private readonly FakePatientRepository _patients = new();
    private readonly FakeBranchRepository _branches = new();
    private readonly FakeLabTestRepository _tests = new();
    private readonly FakeSampleTypeRepository _sampleTypes = new();
    private readonly FakeVisitRepository _visits = new();
    private readonly FakeInvoiceRepository _invoices = new();

    private Patient _patient = null!;
    private SkyLIS.Domain.Org.Branch _branch = null!;
    private LabTest _gluF = null!;
    private LabTest _gluPp = null!;

    private RegisterVisitHandler Arrange()
    {
        _branch = SkyLIS.Domain.Org.Branch.Create(
            Guid.NewGuid(), _tenant.TenantId, "MAIN", "Main Branch", null, null, isMain: true, _clock.UtcNow);
        _branches.Add(_branch);

        var type = SampleType.Create(Guid.NewGuid(), _tenant.TenantId, "Venous blood", "Fluoride");
        var fasting = type.AddCondition(Guid.NewGuid(), "Fasting 8h", null, "VB-G1");
        var pp2h = type.AddCondition(Guid.NewGuid(), "Post-prandial +2h", 120, "VB-G2");
        _sampleTypes.Add(type);

        _gluF = LabTest.CreateFromPlatformSeed(Guid.NewGuid(), _tenant.TenantId, "GLU-F", "Fasting Glucose",
            "Chemistry", type.Id, fasting.Id, Money.Of(80, "EGP"));
        _gluPp = LabTest.CreateFromPlatformSeed(Guid.NewGuid(), _tenant.TenantId, "GLU-PP", "Glucose PP 2h",
            "Chemistry", type.Id, pp2h.Id, Money.Of(80, "EGP"));
        _tests.Add(_gluF);
        _tests.Add(_gluPp);

        _patient = Patient.Register(Guid.NewGuid(), _tenant.TenantId, "PN-0001", "Mona El-Sayed",
            Sex.Female, new DateOnly(1992, 3, 10), PhoneNumber.Of("+201002345678"), null, _clock.UtcNow);
        _patients.Add(_patient);

        return new RegisterVisitHandler(
            _patients, _branches, _tests, _sampleTypes, _visits, _invoices,
            new FakeNumberSeries(), _tenant, _clock);
    }

    [Fact]
    public async Task Registers_visit_with_consolidated_and_reserved_samples_and_issues_invoice()
    {
        var handler = Arrange();

        var result = await handler.Handle(new RegisterVisitCommand(
            _patient.Id, _branch.Id, [_gluF.Id, _gluPp.Id], IsStat: false, StatReason: null), CancellationToken.None);

        result.VisitNumber.Should().Contain("-MAIN-", "number series run per branch (P03.2)");
        result.Samples.Should().HaveCount(2);
        result.Samples.Should().ContainSingle(s => s.State == nameof(SampleState.ReadyToCollect));
        result.Samples.Should().ContainSingle(s =>
            s.State == nameof(SampleState.ConditionPending) && s.ReadyAtUtc != null);
        result.Total.Should().Be(160);
        result.Currency.Should().Be("EGP");

        _visits.Items.Should().ContainSingle();
        _invoices.Items.Should().ContainSingle(i => i.Status == InvoiceStatus.Issued && i.Total.Amount == 160);
        _patient.LastVisitAtUtc.Should().NotBeNull("visit registration stamps the identity-confirmation triple");
    }

    [Fact]
    public async Task Unknown_test_id_is_a_not_found()
    {
        var handler = Arrange();
        var act = () => handler.Handle(
            new RegisterVisitCommand(_patient.Id, _branch.Id, [Guid.NewGuid()], false, null), CancellationToken.None);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Unknown_patient_is_a_not_found()
    {
        var handler = Arrange();
        var act = () => handler.Handle(
            new RegisterVisitCommand(Guid.NewGuid(), _branch.Id, [_gluF.Id], false, null), CancellationToken.None);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Unknown_branch_is_a_not_found()
    {
        var handler = Arrange();
        var act = () => handler.Handle(
            new RegisterVisitCommand(_patient.Id, Guid.NewGuid(), [_gluF.Id], false, null), CancellationToken.None);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Deactivated_branch_is_a_conflict()
    {
        var handler = Arrange();
        var closed = SkyLIS.Domain.Org.Branch.Create(
            Guid.NewGuid(), _tenant.TenantId, "OLD", "Closed Branch", null, null, isMain: false, _clock.UtcNow);
        closed.Deactivate();
        _branches.Add(closed);

        var act = () => handler.Handle(
            new RegisterVisitCommand(_patient.Id, closed.Id, [_gluF.Id], false, null), CancellationToken.None);
        await act.Should().ThrowAsync<ConflictException>().WithMessage("*deactivated*");
    }
}
