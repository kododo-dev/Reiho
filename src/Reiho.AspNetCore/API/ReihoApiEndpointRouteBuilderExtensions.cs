using System.ComponentModel;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;

namespace Kododo.Reiho.AspNetCore.API;

public static class ReihoApiEndpointRouteBuilderExtensions
{
    extension(IEndpointRouteBuilder endpoints)
    {
        public IEndpointRouteBuilder MapRequests(Assembly assembly)
        {
            var map = typeof(ReihoApiEndpointRouteBuilderExtensions).GetMethods()
                .First(m => m is { Name: "Map", IsGenericMethodDefinition: true } && m.GetGenericArguments().Length == 1);
            var mapResult = typeof(ReihoApiEndpointRouteBuilderExtensions).GetMethods()
                .First(m => m is { Name: "Map", IsGenericMethodDefinition: true } && m.GetGenericArguments().Length == 2);

            foreach (var (request, result) in RequestType.Find(assembly))
            {
                var generic = result is null ? map.MakeGenericMethod(request) : mapResult.MakeGenericMethod(request, result);
                generic.Invoke(null, [endpoints, null]);
            }

            return endpoints;
        }

        public IEndpointRouteBuilder Map<TRequest>(string? path = null)
            where  TRequest : IRequest
        {
            endpoints.MapPost(path ?? typeof(TRequest).Name, async (HttpContext ctx, CancellationToken ct) =>
            {
                var (success, request) = await TryCreateRequest<TRequest>(ctx, ct);
                if (!success)
                {
                    ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await ctx.Response.WriteAsync("Request body is required or invalid for this request type.", ct);
                    return;
                }

                var handler = ctx.RequestServices.GetRequiredService<IRequestHandler<TRequest>>();
                await handler.HandleAsync(request!, ct);
                ctx.Response.StatusCode = StatusCodes.Status204NoContent;
            })
            .WithRequestAttributes<TRequest>()
            .Describe<TRequest>()
            .Produces(StatusCodes.Status204NoContent);

            return endpoints;
        }

        public IEndpointRouteBuilder Map<TRequest, TResult>(string? path = null)
            where TRequest : IRequest<TResult>
        {
            endpoints.MapPost(path ?? typeof(TRequest).Name, async (HttpContext ctx, CancellationToken ct) =>
            {
                var (success, request) = await TryCreateRequest<TRequest>(ctx, ct);
                if (!success)
                {
                    ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await ctx.Response.WriteAsync("Request body is required or invalid for this request type.", ct);
                    return;
                }

                var handler = ctx.RequestServices.GetRequiredService<IRequestHandler<TRequest, TResult>>();
                var result = await handler.HandleAsync(request!, ct);
                ctx.Response.ContentType = "application/json";
                await JsonSerializer.SerializeAsync(
                    ctx.Response.Body,
                    result,
                    result?.GetType() ?? typeof(object),
                    options: ReihoJson.Options,
                    cancellationToken: ct
                );
            })
            .WithRequestAttributes<TRequest>()
            .Describe<TRequest>()
            .Produces<TResult>(StatusCodes.Status200OK, "application/json");

            return endpoints;
        }

        private static async Task<(bool Success, TRequest? Request)> TryCreateRequest<TRequest>(HttpContext ctx, CancellationToken ct)
        {
            var hasParameterlessCtor = typeof(TRequest).GetConstructor(Type.EmptyTypes) != null;

            // A chunked body has no Content-Length, so ask the server whether there is a body at all.
            var hasBody = ctx.Request.ContentLength is > 0
                          || (ctx.Request.ContentLength is null
                              && ctx.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody == true);

            if (!hasBody)
            {
                if (hasParameterlessCtor)
                    return (true, (TRequest)Activator.CreateInstance(typeof(TRequest))!);
                else
                    return (false, default);
            }
            else
            {
                try
                {
                    var request = await JsonSerializer.DeserializeAsync<TRequest>(ctx.Request.Body, ReihoJson.Options, cancellationToken: ct);
                    return (request != null, request);
                }
                catch (JsonException)
                {
                    return (false, default);
                }
            }
        }

    }

    // The handler reads the body itself, so tell ApiExplorer and OpenAPI what the endpoint takes and returns.
    private static RouteHandlerBuilder Describe<TRequest>(this RouteHandlerBuilder builder)
    {
        builder
            .Accepts(typeof(TRequest), isOptional: typeof(TRequest).GetConstructor(Type.EmptyTypes) != null, "application/json")
            .Produces<string>(StatusCodes.Status400BadRequest, "text/plain");
        if (typeof(TRequest).GetCustomAttribute<DescriptionAttribute>()?.Description is { } description)
            builder.WithSummary(description);
        // Named after the full route, group prefix included, which is only known when the endpoint is built. The route
        // is unique, so the name is too, even for requests of the same name mapped by several libraries.
        builder.Add(endpoint =>
        {
            if (endpoint is not RouteEndpointBuilder { RoutePattern: var pattern } || endpoint.Metadata.OfType<IEndpointNameMetadata>().Any())
                return;
            // Both, as WithName adds them: ApiExplorer reads the route name, link generation the endpoint name.
            var name = NameOf(pattern);
            endpoint.Metadata.Add(new EndpointNameMetadata(name));
            endpoint.Metadata.Add(new RouteNameMetadata(name));
        });
        return builder;
    }

    /// <summary>The route as an identifier: <c>/api/log-way/GetSources</c> → <c>Api_LogWay_GetSources</c>.</summary>
    internal static string NameOf(RoutePattern pattern)
    {
        var segments = pattern.PathSegments.Select(segment =>
        {
            var name = new StringBuilder();
            foreach (var part in segment.Parts)
            {
                var text = part switch
                {
                    RoutePatternLiteralPart literal => literal.Content,
                    RoutePatternParameterPart parameter => parameter.Name,
                    _ => "",
                };
                var upper = true;
                foreach (var c in text)
                {
                    if (!char.IsLetterOrDigit(c))
                    {
                        upper = true;
                        continue;
                    }
                    name.Append(upper ? char.ToUpperInvariant(c) : c);
                    upper = false;
                }
            }
            return name.ToString();
        });
        return string.Join("_", segments.Where(s => s.Length > 0));
    }

    // The request's attributes, e.g. [Authorize], [AllowAnonymous] or [EnableRateLimiting], apply to its endpoint as
    // they would on a controller action; inherited ones too, so a base request can carry them for all derived requests.
    private static RouteHandlerBuilder WithRequestAttributes<TRequest>(this RouteHandlerBuilder builder) =>
        builder.WithMetadata(typeof(TRequest).GetCustomAttributes(inherit: true));
}
