using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using SkyLIS.Application.Common;
using SkyLIS.Domain.Common;

namespace SkyLIS.Api.Infrastructure;

/// <summary>
/// Maps exceptions to standardized Problem Details. Never exposes stack traces, SQL,
/// connection information, internal class names, secrets, or infrastructure details.
/// </summary>
internal sealed class ApiExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetails;

    public ApiExceptionHandler(IProblemDetailsService problemDetails) => _problemDetails = problemDetails;

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        var (status, title, detail, errors) = exception switch
        {
            RequestValidationException v => (StatusCodes.Status400BadRequest, "Validation failed", v.Message, (object?)v.Errors),
            NotFoundException n => (StatusCodes.Status404NotFound, "Not found", n.Message, null),
            ForbiddenAccessException f => (StatusCodes.Status403Forbidden, "Forbidden", f.Message, null),
            ConflictException c => (StatusCodes.Status409Conflict, "Conflict", c.Message, null),
            InvalidStateTransitionException t => (StatusCodes.Status409Conflict, "Invalid state transition", t.Message, null),
            DomainException d => (StatusCodes.Status422UnprocessableEntity, "Business rule violated", d.Message, null),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred", "The request could not be processed.", null),
        };

        httpContext.Response.StatusCode = status;
        var problem = new ProblemDetails { Status = status, Title = title, Detail = detail };
        if (errors is not null) problem.Extensions["errors"] = errors;

        return await _problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }
}
