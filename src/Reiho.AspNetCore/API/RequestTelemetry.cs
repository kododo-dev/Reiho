using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kododo.Reiho.AspNetCore.API;

/// <summary>
/// Names to observe Reiho by: its traces and its log category.
/// </summary>
/// <example>
/// <code>
/// builder.Services.AddOpenTelemetry().WithTracing(t => t.AddSource(ReihoTelemetry.ActivitySourceName));
/// builder.Logging.AddFilter(ReihoTelemetry.LogCategory, LogLevel.Debug);
/// </code>
/// </example>
public static class ReihoTelemetry
{
    /// <summary>
    /// The source of an activity per request, around its filters and handler, named after the request and tagged with
    /// <c>reiho.request</c> and <c>reiho.transport</c>. The HTTP request's own activity gets <c>reiho.request</c> too.
    /// </summary>
    public const string ActivitySourceName = "Kododo.Reiho";

    /// <summary>The category of the logs, at debug level, of requests turned away: not valid, or ended by a <see cref="RequestException"/>.</summary>
    public const string LogCategory = "Kododo.Reiho.Requests";

    internal static readonly ActivitySource Source = new(ActivitySourceName);

    private static readonly Action<ILogger, string, RequestTransport, string, Exception?> Rejected =
        LoggerMessage.Define<string, RequestTransport, string>(LogLevel.Debug, new EventId(1, "RequestRejected"),
            "{Request} over {Transport} was turned away: {Reason}");

    private static readonly Action<ILogger, string, RequestTransport, int, string, Exception?> Ended =
        LoggerMessage.Define<string, RequestTransport, int, string>(LogLevel.Debug, new EventId(2, "RequestEnded"),
            "{Request} over {Transport} ended with {StatusCode}: {Message}");

    internal static void LogRejected(IServiceProvider services, Type request, RequestTransport transport, string reason)
    {
        if (Logger(services) is { } logger && logger.IsEnabled(LogLevel.Debug))
            Rejected(logger, request.Name, transport, reason, null);
    }

    internal static void LogInvalid(IServiceProvider services, Type request, RequestTransport transport, IReadOnlyDictionary<string, string[]> errors) =>
        LogRejected(services, request, transport, "not valid: " + string.Join(", ", errors.Keys));

    internal static void LogEnded(IServiceProvider services, Type request, RequestTransport transport, RequestException e)
    {
        if (Logger(services) is { } logger && logger.IsEnabled(LogLevel.Debug))
            Ended(logger, request.Name, transport, e.StatusCode, e.Message, null);
    }

    private static ILogger? Logger(IServiceProvider services) =>
        services.GetService<ILoggerFactory>()?.CreateLogger(LogCategory);
}
