using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
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
            });

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
            });

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
}