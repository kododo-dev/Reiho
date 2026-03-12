using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Kododo.Reiho.AspNetCore.Requests;

public static class ReihoRequestsEndpointRouteBuilderExtensions
{
    extension(IEndpointRouteBuilder endpoints)
    {
        public IEndpointRouteBuilder MapRequests(string? path = null)
        {
            return endpoints.MapRequests(Assembly.GetCallingAssembly(), path);
        }

        public IEndpointRouteBuilder MapRequests(Assembly assembly, string? path = null)
        {
            var pathEndpoints = endpoints;
            if (!string.IsNullOrEmpty(path))
            {
                pathEndpoints = endpoints.MapGroup(path);
            }

            // IRequest
            var requestTypes = assembly.GetTypes()
                .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IRequest).IsAssignableFrom(t) && !t.IsGenericType);

            var mapMethod = typeof(ReihoRequestsEndpointRouteBuilderExtensions)
                .GetMethods()
                .First(m => m is { Name: "Map", IsGenericMethodDefinition: true } && m.GetGenericArguments().Length == 1);

            foreach (var requestType in requestTypes)
            {
                var genericMap = mapMethod.MakeGenericMethod(requestType);
                genericMap.Invoke(null, [pathEndpoints, null]);
            }

            // IRequest<TResult>
            var requestResultTypes = assembly.GetTypes()
                .Where(t => t is { IsClass: true, IsAbstract: false } &&
                            t.GetInterfaces().Any(i =>
                                i.IsGenericType &&
                                i.GetGenericTypeDefinition() == typeof(IRequest<>)) &&
                            !t.IsGenericType);

            var mapResultMethod = typeof(ReihoRequestsEndpointRouteBuilderExtensions)
                .GetMethods()
                .First(m => m is { Name: "Map", IsGenericMethodDefinition: true } && m.GetGenericArguments().Length == 2);

            foreach (var requestType in requestResultTypes)
            {
                var iRequestInterface = requestType.GetInterfaces()
                    .First(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>));

                var resultType = iRequestInterface.GetGenericArguments()[0];

                var genericMap = mapResultMethod.MakeGenericMethod(requestType, resultType);
                genericMap.Invoke(null, [pathEndpoints, null]);
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
                await ctx.Response.WriteAsJsonAsync(result, cancellationToken: ct);
            });

            return endpoints;
        }

        private static async Task<(bool Success, TRequest? Request)> TryCreateRequest<TRequest>(HttpContext ctx, CancellationToken ct)
        {
            var hasParameterlessCtor = typeof(TRequest).GetConstructor(Type.EmptyTypes) != null;

            if (ctx.Request.ContentLength == 0)
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
                    var request = await ctx.Request.ReadFromJsonAsync<TRequest>(cancellationToken: ct);
                    return (request != null, request);
                }
                catch
                {
                    return (false, default);
                }
            }
        }

    }
}