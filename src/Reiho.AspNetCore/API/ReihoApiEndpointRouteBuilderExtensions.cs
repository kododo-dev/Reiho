using System.ComponentModel;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
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
            endpoints.MapPost(path ?? typeof(TRequest).Name, (HttpContext ctx, CancellationToken ct) =>
                HandleAsync<TRequest>(ctx, ct, async request =>
                {
                    var handler = ctx.RequestServices.GetRequiredService<IRequestHandler<TRequest>>();
                    await handler.HandleAsync(request, ct);
                    ctx.Response.StatusCode = StatusCodes.Status204NoContent;
                }))
            .WithFileMetadata<TRequest>()
            .WithRequestAttributes<TRequest>()
            .Describe<TRequest>()
            .Produces(StatusCodes.Status204NoContent);

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
                    if (result is FileResponse file)
                    {
                        await TypedResults.Stream(file.Content, file.ContentType, file.FileName).ExecuteAsync(ctx);
                        return;
                    }
                    ctx.Response.ContentType = "application/json";
                    await JsonSerializer.SerializeAsync(
                        ctx.Response.Body,
                        result,
                        result?.GetType() ?? typeof(object),
                        options: ReihoJson.Options,
                        cancellationToken: ct
                    );
                }))
            .WithFileMetadata<TRequest>()
            .WithRequestAttributes<TRequest>()
            .Describe<TRequest>()
            .ProducesResult<TResult>();

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
                    ? TypedResults.ValidationProblem(RequestValidator.ByJsonName(fieldErrors))
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

            // Only a request that receives files takes a form: a form needs no CORS preflight, so it is open to
            // cross-site posts, which the endpoint's antiforgery metadata guards against.
            if (RequestFiles.Of(typeof(TRequest)).Length > 0)
            {
                // The antiforgery middleware only records its verdict, and the form refuses to be read after a failed
                // one; as for minimal APIs, the endpoint turns the post away.
                if (ctx.Features.Get<IAntiforgeryValidationFeature>() is { IsValid: false })
                    return (default, TypedResults.Problem(detail: "The antiforgery token is missing or not valid.", statusCode: StatusCodes.Status400BadRequest));
                if (ctx.Request.HasFormContentType)
                    return await ReadFormAsync<TRequest>(ctx, ct);
            }

            try
            {
                var request = await JsonSerializer.DeserializeAsync<TRequest>(ctx.Request.Body, ReihoJson.Options, cancellationToken: ct);
                return request is null ? (default, BodyRequired()) : (request, null);
            }
            catch (Exception e) when (e is JsonException or NotSupportedException)
            {
                return (default, InvalidBody(e));
            }
        }

        private static async Task<(TRequest? Request, IResult? Problem)> ReadFormAsync<TRequest>(HttpContext ctx, CancellationToken ct)
        {
            IFormCollection form;
            try
            {
                form = await ctx.Request.ReadFormAsync(ct);
            }
            catch (InvalidDataException)
            {
                return (default, TypedResults.Problem(detail: "The form is not valid.", statusCode: StatusCodes.Status400BadRequest));
            }

            TRequest? request;
            try
            {
                request = RequestFiles.Fields(typeof(TRequest), form).Deserialize<TRequest>(ReihoJson.Options);
            }
            catch (Exception e) when (e is JsonException or NotSupportedException)
            {
                return (default, InvalidBody(e));
            }
            if (request is null)
                return (default, BodyRequired());

            RequestFiles.SetFiles(request, form.Files);
            return (request, null);
        }

    }

    // A new result each time: the problem details service adds to the ProblemDetails it writes, e.g. a trace id.
    private static IResult BodyRequired() =>
        TypedResults.Problem(detail: "A JSON body is required for this request.", statusCode: StatusCodes.Status400BadRequest);

    // The path names the field when the JSON is well formed but a value does not fit; the message is not returned, as
    // it names .NET types.
    private static IResult InvalidBody(Exception e) =>
        e is JsonException { Path: { Length: > 2 } path } && path.StartsWith("$.")
            ? TypedResults.ValidationProblem(new Dictionary<string, string[]> { [path[2..]] = ["The value is not valid."] })
            : TypedResults.Problem(detail: "The request body is not valid JSON for this request.", statusCode: StatusCodes.Status400BadRequest);

    // Minimal APIs require an antiforgery token for form posts; so does a request that receives files, unless it says
    // otherwise with [RequireAntiforgeryToken(false)], which, as a request attribute added later, takes precedence.
    private static RouteHandlerBuilder WithFileMetadata<TRequest>(this RouteHandlerBuilder builder) =>
        RequestFiles.Of(typeof(TRequest)).Length > 0 ? builder.WithMetadata(new RequireAntiforgeryTokenAttribute()) : builder;

    private static RouteHandlerBuilder ProducesResult<TResult>(this RouteHandlerBuilder builder) =>
        typeof(FileResponse).IsAssignableFrom(typeof(TResult))
            ? builder.Produces(StatusCodes.Status200OK, typeof(Stream), "application/octet-stream")
            : builder.Produces<TResult>(StatusCodes.Status200OK, "application/json");

    // The handler reads the body itself, so tell ApiExplorer and OpenAPI what the endpoint takes and returns.
    private static RouteHandlerBuilder Describe<TRequest>(this RouteHandlerBuilder builder)
    {
        var form = RequestFiles.Of(typeof(TRequest)).Length > 0;
        builder
            .Accepts(typeof(TRequest), isOptional: typeof(TRequest).GetConstructor(Type.EmptyTypes) != null,
                form ? "multipart/form-data" : "application/json")
            .ProducesValidationProblem();
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
