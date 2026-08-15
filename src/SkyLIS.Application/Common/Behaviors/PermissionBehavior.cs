using MediatR;

namespace SkyLIS.Application.Common.Behaviors;

/// <summary>
/// Backend authorization: requests declaring IRequirePermission are checked against the
/// caller's granted permissions. The frontend is never the security boundary (EAA rule).
/// Platform-scoped requests additionally require a platform operator identity.
/// </summary>
public sealed class PermissionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ICurrentUser _currentUser;

    public PermissionBehavior(ICurrentUser currentUser) => _currentUser = currentUser;

    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (request is IPlatformScoped && !_currentUser.IsPlatformOperator)
            throw new ForbiddenAccessException("This operation is restricted to platform operators.");

        if (request is IRequirePermission gated && !_currentUser.HasPermission(gated.Permission))
            throw new ForbiddenAccessException($"Missing permission '{gated.Permission}'.");

        return next();
    }
}
