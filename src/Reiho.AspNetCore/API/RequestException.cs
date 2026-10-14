using Microsoft.AspNetCore.Http;

namespace Kododo.Reiho.AspNetCore.API;

/// <summary>
/// Ends a request with an error the caller can act on. Thrown by a handler, it becomes a problem details response
/// (RFC 9457) with <see cref="StatusCode"/> and the message as its detail, or, with <see cref="Errors"/>, a validation
/// problem listing them per field. Other exceptions are left to the application, e.g. its exception handler.
/// </summary>
/// <example>
/// <code>
/// var user = await users.FindAsync(request.Id, ct) ?? throw RequestException.NotFound($"User {request.Id} does not exist.");
/// if (taken) throw RequestException.Invalid(nameof(request.Name), "The name is already taken.");
/// </code>
/// </example>
public class RequestException(string message, int statusCode = StatusCodes.Status400BadRequest) : Exception(message)
{
    /// <summary>HTTP status code of the response; 400 by default.</summary>
    public int StatusCode { get; } = statusCode;

    /// <summary>Messages per field of the request, as in a validation problem; <c>null</c> for an error of the whole request.</summary>
    public IReadOnlyDictionary<string, string[]>? Errors { get; init; }

    /// <summary>404: what the request refers to does not exist.</summary>
    public static RequestException NotFound(string message) => new(message, StatusCodes.Status404NotFound);

    /// <summary>409: the request conflicts with the current state, e.g. a duplicate or a stale version.</summary>
    public static RequestException Conflict(string message) => new(message, StatusCodes.Status409Conflict);

    /// <summary>403: the user may not do this.</summary>
    public static RequestException Forbidden(string message) => new(message, StatusCodes.Status403Forbidden);

    /// <summary>400 with <paramref name="message"/> for <paramref name="field"/>, e.g. a check that needs the database.</summary>
    public static RequestException Invalid(string field, string message) =>
        new(message) { Errors = new Dictionary<string, string[]> { [field] = [message] } };
}
