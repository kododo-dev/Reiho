using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kododo.Reiho.AspNetCore.API;

/// <summary>
/// Runs around the handler of every request, over HTTP and as an MCP tool alike, once the request has been read and
/// validated: for logging, auditing, timing or checks of its own. Unlike an endpoint filter, it sees the request object.
/// Register it with <c>AddRequestFilter&lt;T&gt;()</c>; filters run in the order they were registered, each scoped to the
/// request.
/// </summary>
/// <example>
/// <code>
/// public sealed class AuditFilter(ILogger&lt;AuditFilter&gt; logger) : IRequestFilter
/// {
///     public async ValueTask&lt;object?&gt; InvokeAsync(RequestFilterContext context, RequestFilterDelegate next)
///     {
///         var result = await next(context);
///         logger.LogInformation("{Request} done", context.Request.GetType().Name);
///         return result;
///     }
/// }
/// </code>
/// </example>
public interface IRequestFilter
{
    /// <summary>
    /// Calls <paramref name="next"/> to go on, and returns its result: the handler's, or <c>null</c> for an
    /// <see cref="IRequest"/>. Throw a <see cref="RequestException"/> to stop the request with an error; a filter may
    /// also return a result without calling <paramref name="next"/>, of the request's result type.
    /// </summary>
    ValueTask<object?> InvokeAsync(RequestFilterContext context, RequestFilterDelegate next);
}

/// <summary>The rest of the pipeline: the next filter, or the handler.</summary>
public delegate ValueTask<object?> RequestFilterDelegate(RequestFilterContext context);

/// <summary>The request a filter runs around, and where it came from.</summary>
public sealed class RequestFilterContext(object request, RequestTransport transport, IServiceProvider services, HttpContext? httpContext, CancellationToken cancellationToken)
{
    /// <summary>The request, read and validated.</summary>
    public object Request { get; } = request;

    public RequestTransport Transport { get; } = transport;

    /// <summary>Services of the request's scope.</summary>
    public IServiceProvider Services { get; } = services;

    /// <summary>The HTTP request; <c>null</c> for an MCP tool call.</summary>
    public HttpContext? HttpContext { get; } = httpContext;

    public CancellationToken CancellationToken { get; } = cancellationToken;
}

/// <summary>How a request arrived.</summary>
public enum RequestTransport
{
    /// <summary>Its HTTP endpoint.</summary>
    Http,

    /// <summary>A call of its MCP tool.</summary>
    Mcp,
}

public static class ReihoRequestFilterServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>Runs <typeparamref name="TFilter"/> around every request's handler; filters run in the order they are added.</summary>
        public IServiceCollection AddRequestFilter<TFilter>() where TFilter : class, IRequestFilter
        {
            services.TryAddEnumerable(ServiceDescriptor.Scoped<IRequestFilter, TFilter>());
            return services;
        }
    }
}

/// <summary>Runs the registered filters around a handler.</summary>
internal static class RequestPipeline
{
    public static ValueTask<object?> InvokeAsync(RequestFilterContext context, Func<ValueTask<object?>> handler)
    {
        var filters = context.Services.GetServices<IRequestFilter>().ToArray();
        if (filters.Length == 0)
            return handler();

        RequestFilterDelegate next = _ => handler();
        for (var i = filters.Length - 1; i >= 0; i--)
        {
            var filter = filters[i];
            var rest = next;
            next = c => filter.InvokeAsync(c, rest);
        }
        return next(context);
    }
}
