using MediatR;

namespace SkyLIS.Application.Common;

/// <summary>A command changes state. Handled in a transaction; SaveChanges via UnitOfWorkBehavior.</summary>
public interface ICommand<out TResponse> : IRequest<TResponse> { }

/// <summary>A query retrieves data without changing state; projects directly to DTOs.</summary>
public interface IQuery<out TResponse> : IRequest<TResponse> { }

/// <summary>Declares the permission required to execute a request (checked by PermissionBehavior).</summary>
public interface IRequirePermission
{
    string Permission { get; }
}

/// <summary>Marks a request as platform-scoped (Admin Portal): no tenant context required.</summary>
public interface IPlatformScoped { }
