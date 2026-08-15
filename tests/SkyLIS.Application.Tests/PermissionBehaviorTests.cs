using FluentAssertions;
using SkyLIS.Application.Common;
using SkyLIS.Application.Common.Behaviors;
using Xunit;

namespace SkyLIS.Application.Tests;

public class PermissionBehaviorTests
{
    private sealed record GatedCommand : ICommand<string>, IRequirePermission
    {
        public string Permission => "orders.visit.create";
    }

    private sealed record PlatformCommand : ICommand<string>, IPlatformScoped;

    private sealed class StubUser : ICurrentUser
    {
        public Guid? UserId => Guid.NewGuid();
        public Guid? TenantId => Guid.NewGuid();
        public bool IsPlatformOperator { get; init; }
        public HashSet<string> Permissions { get; init; } = [];
        public bool HasPermission(string permission) => Permissions.Contains(permission);
    }

    [Fact]
    public async Task Missing_permission_is_forbidden()
    {
        var behavior = new PermissionBehavior<GatedCommand, string>(new StubUser());
        var act = () => behavior.Handle(new GatedCommand(), () => Task.FromResult("ok"), CancellationToken.None);
        await act.Should().ThrowAsync<ForbiddenAccessException>().WithMessage("*orders.visit.create*");
    }

    [Fact]
    public async Task Granted_permission_passes_through()
    {
        var behavior = new PermissionBehavior<GatedCommand, string>(
            new StubUser { Permissions = ["orders.visit.create"] });
        var result = await behavior.Handle(new GatedCommand(), () => Task.FromResult("ok"), CancellationToken.None);
        result.Should().Be("ok");
    }

    [Fact]
    public async Task Platform_scope_requires_platform_operator()
    {
        var behavior = new PermissionBehavior<PlatformCommand, string>(new StubUser { IsPlatformOperator = false });
        var act = () => behavior.Handle(new PlatformCommand(), () => Task.FromResult("ok"), CancellationToken.None);
        await act.Should().ThrowAsync<ForbiddenAccessException>().WithMessage("*platform operators*");
    }

    [Fact]
    public async Task Platform_operator_passes_platform_scope()
    {
        var behavior = new PermissionBehavior<PlatformCommand, string>(new StubUser { IsPlatformOperator = true });
        var result = await behavior.Handle(new PlatformCommand(), () => Task.FromResult("ok"), CancellationToken.None);
        result.Should().Be("ok");
    }
}
