using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace SkyLIS.Architecture.Tests;

/// <summary>
/// Enforces the Enterprise Application Architect dependency rules (NFR-013).
/// These tests ARE the architecture gate: a violation fails CI.
/// </summary>
public class ArchitectureTests
{
    private static readonly Assembly Domain = typeof(SkyLIS.Domain.Common.AggregateRoot).Assembly;
    private static readonly Assembly Application = typeof(SkyLIS.Application.DependencyInjection).Assembly;
    private static readonly Assembly Infrastructure = typeof(SkyLIS.Infrastructure.DependencyInjection).Assembly;
    private static readonly Assembly Api = typeof(SkyLIS.Api.Infrastructure.TenantResolutionMiddleware).Assembly;

    [Fact]
    public void Domain_is_framework_pure()
    {
        // The Domain layer must not reference ASP.NET Core, EF Core, MediatR, Serilog,
        // PostgreSQL libraries, infrastructure SDKs, HTTP concepts, or API models.
        var forbidden = new[]
        {
            "Microsoft.EntityFrameworkCore", "MediatR", "Serilog", "Npgsql",
            "Microsoft.AspNetCore", "System.Net.Http", "FluentValidation",
        };

        var referenced = Domain.GetReferencedAssemblies().Select(a => a.Name!).ToList();
        referenced.Should().NotContain(name =>
            forbidden.Any(f => name.StartsWith(f, StringComparison.Ordinal)),
            "the Domain layer is framework-pure by the EAA standard");
    }

    [Fact]
    public void Domain_does_not_depend_on_outer_layers()
    {
        var result = Types.InAssembly(Domain)
            .ShouldNot().HaveDependencyOnAny("SkyLIS.Application", "SkyLIS.Infrastructure", "SkyLIS.Api")
            .GetResult();
        result.IsSuccessful.Should().BeTrue(Explain(result));
    }

    [Fact]
    public void Application_does_not_depend_on_infrastructure_or_api()
    {
        var result = Types.InAssembly(Application)
            .ShouldNot().HaveDependencyOnAny("SkyLIS.Infrastructure", "SkyLIS.Api")
            .GetResult();
        result.IsSuccessful.Should().BeTrue(Explain(result));
    }

    [Fact]
    public void Application_does_not_reference_ef_core()
    {
        var referenced = Application.GetReferencedAssemblies().Select(a => a.Name!).ToList();
        referenced.Should().NotContain(name =>
            name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) ||
            name.StartsWith("Npgsql", StringComparison.Ordinal),
            "persistence is an Infrastructure concern behind Application-owned ports");
    }

    [Fact]
    public void Infrastructure_does_not_depend_on_api()
    {
        var result = Types.InAssembly(Infrastructure)
            .ShouldNot().HaveDependencyOn("SkyLIS.Api")
            .GetResult();
        result.IsSuccessful.Should().BeTrue(Explain(result));
    }

    [Fact]
    public void Command_and_query_handlers_are_not_public()
    {
        var result = Types.InAssembly(Application)
            .That().HaveNameEndingWith("Handler")
            .Should().NotBePublic()
            .GetResult();
        result.IsSuccessful.Should().BeTrue(
            "handlers are implementation details reached only through MediatR" + Explain(result));
    }

    [Fact]
    public void Validators_are_not_public()
    {
        var result = Types.InAssembly(Application)
            .That().HaveNameEndingWith("Validator")
            .Should().NotBePublic()
            .GetResult();
        result.IsSuccessful.Should().BeTrue(Explain(result));
    }

    [Fact]
    public void Domain_entities_never_cross_the_api_wire()
    {
        // Endpoint request/response records live in the API layer; aggregates must not
        // appear as endpoint parameter or return types. Approximated here by checking
        // the API assembly declares no public type deriving from AggregateRoot or Entity.
        var result = Types.InAssembly(Api)
            .Should().NotInherit(typeof(SkyLIS.Domain.Common.AggregateRoot))
            .And().NotInherit(typeof(SkyLIS.Domain.Common.Entity))
            .GetResult();
        result.IsSuccessful.Should().BeTrue(Explain(result));
    }

    private static string Explain(TestResult result) =>
        result.IsSuccessful ? string.Empty :
            " — violations: " + string.Join(", ", result.FailingTypes?.Select(t => t.FullName ?? "?") ?? []);
}
