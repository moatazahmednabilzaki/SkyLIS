namespace SkyLIS.Application.Common;

/// <summary>Mapped to HTTP 404 by the API layer.</summary>
public sealed class NotFoundException : Exception
{
    public NotFoundException(string entity, object key)
        : base($"{entity} '{key}' was not found.") { }
}

/// <summary>Mapped to HTTP 403 by the API layer.</summary>
public sealed class ForbiddenAccessException : Exception
{
    public ForbiddenAccessException(string message) : base(message) { }
}

/// <summary>Mapped to HTTP 409 by the API layer.</summary>
public sealed class ConflictException : Exception
{
    public ConflictException(string message) : base(message) { }
}

/// <summary>Mapped to HTTP 400 with field errors by the API layer.</summary>
public sealed class RequestValidationException : Exception
{
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public RequestValidationException(IReadOnlyDictionary<string, string[]> errors)
        : base("One or more validation failures occurred.") => Errors = errors;
}
