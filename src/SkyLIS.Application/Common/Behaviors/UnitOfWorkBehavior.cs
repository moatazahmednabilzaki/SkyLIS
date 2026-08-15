using MediatR;

namespace SkyLIS.Application.Common.Behaviors;

/// <summary>
/// Commits the aggregate transaction once per command. Queries never save.
/// Domain events collected during the transaction are written to the outbox
/// by the persistence layer inside the same SaveChanges (atomic).
/// </summary>
public sealed class UnitOfWorkBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IUnitOfWork _unitOfWork;

    public UnitOfWorkBehavior(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var response = await next();
        if (IsCommand())
            await _unitOfWork.SaveChangesAsync(ct);
        return response;
    }

    private static bool IsCommand() =>
        typeof(TRequest).GetInterfaces().Any(i =>
            i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>));
}
