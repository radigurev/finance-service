namespace Finance.Infrastructure.Web.ErrorMapping;

/// <summary>
/// Maps a machine-readable error code (the <c>title</c> of a ProblemDetails response) to the HTTP
/// status code returned to the client (SDD-INFRA-009 §2.4). DI-registered and overridable.
/// </summary>
public interface IErrorCodeToStatusMap
{
    /// <summary>Resolves the HTTP status code for the supplied error code.</summary>
    /// <param name="errorCode">The SCREAMING_SNAKE_CASE error code.</param>
    /// <returns>The mapped HTTP status code.</returns>
    int MapToStatus(string errorCode);
}
