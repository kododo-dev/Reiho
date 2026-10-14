using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Kododo.Reiho.AspNetCore.API;

public static class ReihoApiEndpointRouteBuilderExtensions
{
    // One shared instance: System.Text.Json caches type metadata per options instance.
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    
    extension(IEndpointRouteBuilder endpoints)
    {
        public IEndpointRouteBuilder MapRequests(Assembly assembly)
        {
            // IRequest
            var requestTypes = assembly.GetTypes()
                .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IRequest).IsAssignableFrom(t) && !t.IsGenericType);

            var mapMethod = typeof(ReihoApiEndpointRouteBuilderExtensions)
                .GetMethods()
                .First(m => m is { Name: "Map", IsGenericMethodDefinition: true } && m.GetGenericArguments().Length == 1);

            foreach (var requestType in requestTypes)
            {
                var genericMap = mapMethod.MakeGenericMethod(requestType);
                genericMap.Invoke(null, [endpoints, null]);
            }

            // IRequest<TResult>
            var requestResultTypes = assembly.GetTypes()
                .Where(t => t is { IsClass: true, IsAbstract: false } &&
                            t.GetInterfaces().Any(i =>
                                i.IsGenericType &&
                                i.GetGenericTypeDefinition() == typeof(IRequest<>)) &&
                            !t.IsGenericType);

            var mapResultMethod = typeof(ReihoApiEndpointRouteBuilderExtensions)
                .GetMethods()
                .First(m => m is { Name: "Map", IsGenericMethodDefinition: true } && m.GetGenericArguments().Length == 2);

            foreach (var requestType in requestResultTypes)
            {
                var iRequestInterface = requestType.GetInterfaces()
                    .First(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>));

                var resultType = iRequestInterface.GetGenericArguments()[0];

                var genericMap = mapResultMethod.MakeGenericMethod(requestType, resultType);
                genericMap.Invoke(null, [endpoints, null]);
            }

            return endpoints;
        }

        public IEndpointRouteBuilder Map<TRequest>(string? path = null)
            where  TRequest : IRequest
        {
            endpoints.MapPost(path ?? typeof(TRequest).Name, (HttpContext ctx, CancellationToken ct) =>
                HandleAsync<TRequest>(ctx, ct, async request =>
                {
                    var handler = ctx.RequestServices.GetRequiredService<IRequestHandler<TRequest>>();
                    await handler.HandleAsync(request, ct);
                    ctx.Response.StatusCode = StatusCodes.Status204NoContent;
                }));

            return endpoints;
        }

        public IEndpointRouteBuilder Map<TRequest, TResult>(string? path = null)
            where TRequest : IRequest<TResult>
        {
            endpoints.MapPost(path ?? typeof(TRequest).Name, (HttpContext ctx, CancellationToken ct) =>
                HandleAsync<TRequest>(ctx, ct, async request =>
                {
                    var handler = ctx.RequestServices.GetRequiredService<IRequestHandler<TRequest, TResult>>();
                    var result = await handler.HandleAsync(request, ct);
                    ctx.Response.ContentType = "application/json";
                    await JsonSerializer.SerializeAsync(
                        ctx.Response.Body,
                        result,
                        result?.GetType() ?? typeof(object),
                        options: SerializerOptions,
                        cancellationToken: ct
                    );
                }));

            return endpoints;
        }

        // Reads and validates the request, runs the handler, and answers errors the caller can act on with problem
        // details; through IProblemDetailsService when the app has AddProblemDetails().
        private static async Task HandleAsync<TRequest>(HttpContext ctx, CancellationToken ct, Func<TRequest, Task> handle)
        {
            var (request, problem) = await ReadRequestAsync<TRequest>(ctx, ct);
            if (problem is not null)
            {
                await problem.ExecuteAsync(ctx);
                return;
            }

            if (RequestValidator.Validate(request!) is { } errors)
            {
                await TypedResults.ValidationProblem(errors).ExecuteAsync(ctx);
                return;
            }

            try
            {
                await handle(request!);
            }
            catch (RequestException e) when (!ctx.Response.HasStarted)
            {
                IResult result = e.Errors is { } fieldErrors
                    ? TypedResults.ValidationProblem(fieldErrors.ToDictionary(f => JsonNamingPolicy.CamelCase.ConvertName(f.Key), f => f.Value))
                    : TypedResults.Problem(detail: e.Message, statusCode: e.StatusCode);
                await result.ExecuteAsync(ctx);
            }
        }

        private static async Task<(TRequest? Request, IResult? Problem)> ReadRequestAsync<TRequest>(HttpContext ctx, CancellationToken ct)
        {
            var hasParameterlessCtor = typeof(TRequest).GetConstructor(Type.EmptyTypes) != null;

            // A chunked body has no Content-Length, so ask the server whether there is a body at all.
            var hasBody = ctx.Request.ContentLength is > 0
                          || (ctx.Request.ContentLength is null
                              && ctx.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody == true);

            if (!hasBody)
            {
                return hasParameterlessCtor
                    ? ((TRequest)Activator.CreateInstance(typeof(TRequest))!, null)
                    : (default, BodyRequired());
            }

            try
            {
                var request = await JsonSerializer.DeserializeAsync<TRequest>(ctx.Request.Body, SerializerOptions, cancellationToken: ct);
                return request is null ? (default, BodyRequired()) : (request, null);
            }
            catch (JsonException e)
            {
                // The path names the field when the JSON is well formed but a value does not fit; the message is not
                // returned, as it names .NET types.
                var field = e.Path is { Length: > 2 } path && path.StartsWith("$.") ? path[2..] : null;
                return (default, field is null
                    ? TypedResults.Problem(detail: "The request body is not valid JSON for this request.", statusCode: StatusCodes.Status400BadRequest)
                    : TypedResults.ValidationProblem(new Dictionary<string, string[]> { [field] = ["The value is not valid."] }));
            }
        }

    }

    // A new result each time: the problem details service adds to the ProblemDetails it writes, e.g. a trace id.
    private static IResult BodyRequired() =>
        TypedResults.Problem(detail: "A JSON body is required for this request.", statusCode: StatusCodes.Status400BadRequest);
}
