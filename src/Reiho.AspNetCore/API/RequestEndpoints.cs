using Microsoft.AspNetCore.Builder;

namespace Kododo.Reiho.AspNetCore.API;

/// <summary>
/// The endpoints <c>MapRequests</c> mapped. Conventions added to it, e.g. <c>RequireAuthorization()</c> or
/// <c>WithTags()</c>, apply to all of them; <see cref="For{TRequest}"/> configures the endpoint of one request.
/// </summary>
/// <example>
/// <code>
/// var requests = app.MapGroup("/api").MapRequests(typeof(Program).Assembly);
/// requests.WithTags("Orders");
/// requests.For&lt;DeleteOrder&gt;().RequireAuthorization("Admin");
/// </code>
/// </example>
public sealed class RequestEndpoints : IEndpointConventionBuilder
{
    private readonly Dictionary<Type, RouteHandlerBuilder> _endpoints;

    internal RequestEndpoints(Dictionary<Type, RouteHandlerBuilder> endpoints) => _endpoints = endpoints;

    /// <summary>The endpoint of each mapped request type.</summary>
    public IReadOnlyDictionary<Type, RouteHandlerBuilder> Endpoints => _endpoints;

    /// <summary>The endpoint of <typeparamref name="TRequest"/>.</summary>
    /// <exception cref="KeyNotFoundException">The request was not mapped, e.g. it is in another assembly.</exception>
    public RouteHandlerBuilder For<TRequest>() =>
        _endpoints.TryGetValue(typeof(TRequest), out var endpoint)
            ? endpoint
            : throw new KeyNotFoundException($"{typeof(TRequest).FullName} was not mapped by this MapRequests call.");

    public void Add(Action<EndpointBuilder> convention)
    {
        foreach (var endpoint in _endpoints.Values)
            endpoint.Add(convention);
    }

    public void Finally(Action<EndpointBuilder> finallyConvention)
    {
        foreach (var endpoint in _endpoints.Values)
            endpoint.Finally(finallyConvention);
    }
}
