using FluentAssertions;
using SkyLIS.Domain.Common;
using SkyLIS.Domain.Org;
using SkyLIS.Domain.Platform;
using Xunit;

namespace SkyLIS.Domain.Tests;

public class BranchTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_normalizes_the_code_and_raises_the_event()
    {
        var branch = Branch.Create(Guid.NewGuid(), TenantId, "zmlk", "Zamalek Branch", "26 July St.", "+20221234567", false, Now);

        branch.Code.Should().Be("ZMLK");
        branch.IsActive.Should().BeTrue();
        branch.DomainEvents.OfType<BranchCreated>().Should().ContainSingle(e => e.Code == "ZMLK");
    }

    [Theory]
    [InlineData("A")]
    [InlineData("TOOLONGCODE1")]
    [InlineData("BAD-CODE")]
    public void Invalid_codes_are_rejected(string code)
    {
        var act = () => Branch.Create(Guid.NewGuid(), TenantId, code, "Branch", null, null, false, Now);
        act.Should().Throw<DomainException>().WithMessage("*Branch code*");
    }

    [Fact]
    public void Departments_dedupe_by_code()
    {
        var branch = Branch.Create(Guid.NewGuid(), TenantId, "MAIN", "Main", null, null, true, Now);
        branch.AddDepartment(Guid.NewGuid(), "CHEM", "Chemistry");

        var act = () => branch.AddDepartment(Guid.NewGuid(), "chem", "Clinical Chemistry");
        act.Should().Throw<DomainException>().WithMessage("*already exists*");
    }

    [Fact]
    public void Main_branch_cannot_be_deactivated()
    {
        var main = Branch.Create(Guid.NewGuid(), TenantId, "MAIN", "Main", null, null, isMain: true, Now);
        var act = () => main.Deactivate();
        act.Should().Throw<DomainException>().WithMessage("*main branch*");
    }

    [Fact]
    public void Secondary_branch_deactivates_and_reactivates()
    {
        var branch = Branch.Create(Guid.NewGuid(), TenantId, "ZMLK", "Zamalek", null, null, isMain: false, Now);
        branch.Deactivate();
        branch.IsActive.Should().BeFalse();
        branch.Activate();
        branch.IsActive.Should().BeTrue();
    }
}

public class CountryPackTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 9, 0, 0, TimeSpan.Zero);

    private static IReadOnlyList<PackSampleType> Content() =>
        [new PackSampleType("Serum", "SST (gold)", [new PackCondition("Fasting 8h", null, "SR-G1")])];

    [Fact]
    public void Create_normalizes_codes_and_starts_at_version_1()
    {
        var pack = CountryPack.Create(Guid.NewGuid(), "eg", "Egypt Defaults", "egp", Content(), Now);

        pack.CountryCode.Should().Be("EG");
        pack.Currency.Should().Be("EGP");
        pack.Version.Should().Be(1);
        pack.SampleTypes.Should().ContainSingle(s => s.Name == "Serum");
    }

    [Fact]
    public void Replace_content_bumps_the_version()
    {
        var pack = CountryPack.Create(Guid.NewGuid(), "EG", "Egypt Defaults", "EGP", Content(), Now);
        pack.ReplaceContent("Egypt Defaults v2", "EGP",
            [new PackSampleType("Urine (random)", "Sterile cup", [new PackCondition("Random", null, "UR-G1")])],
            Now.AddDays(1));

        pack.Version.Should().Be(2);
        pack.Name.Should().Be("Egypt Defaults v2");
        pack.SampleTypes.Should().ContainSingle(s => s.Name == "Urine (random)");
    }

    [Fact]
    public void Empty_content_is_rejected()
    {
        var act = () => CountryPack.Create(Guid.NewGuid(), "EG", "Egypt", "EGP", [], Now);
        act.Should().Throw<DomainException>().WithMessage("*at least one*");
    }

    [Fact]
    public void Conditions_require_group_and_sane_delay()
    {
        var act = () => CountryPack.Create(Guid.NewGuid(), "EG", "Egypt", "EGP",
            [new PackSampleType("Serum", "SST", [new PackCondition("PP", 999999, "SR-G2")])], Now);
        act.Should().Throw<DomainException>().WithMessage("*within 24h*");
    }
}
