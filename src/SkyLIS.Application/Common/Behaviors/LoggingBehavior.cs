using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace SkyLIS.Application.Common.Behaviors;

/// <summary>Structured request logging: operation, tenant, user, duration, outcome.</summary>
public sealed class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;
    private readonly ICurrentUser _currentUser;

    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger, ICurrentUser currentUser)
    {
        _logger = logger;
        _currentUser = currentUser;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var operation = typeof(TRequest).Name;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await next();
            _logger.LogInformation(
                "Handled {Operation} for tenant {TenantId} user {UserId} in {DurationMs} ms: success",
                operation, _currentUser.TenantId, _currentUser.UserId, stopwatch.ElapsedMilliseconds);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Handled {Operation} for tenant {TenantId} user {UserId} in {DurationMs} ms: {Outcome}",
                operation, _currentUser.TenantId, _currentUser.UserId, stopwatch.ElapsedMilliseconds, ex.GetType().Name);
            throw;
        }
    }
}
