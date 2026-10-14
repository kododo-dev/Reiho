using System.ComponentModel;
using Kododo.Reiho.AspNetCore.API;
using Microsoft.AspNetCore.Authorization;

namespace Kododo.Reiho.AspNetCore.Tests.API;

// ── No-result, parameterless ──────────────────────────────────────────────────

public sealed record PingRequest : IRequest;

public sealed class PingHandler : IRequestHandler<PingRequest>
{
    public static int CallCount;

    public Task HandleAsync(PingRequest request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref CallCount);
        return Task.CompletedTask;
    }
}

// ── No-result, required constructor parameter (no parameterless ctor) ─────────

public sealed record DeleteItemRequest(int Id) : IRequest;

public sealed class DeleteItemHandler : IRequestHandler<DeleteItemRequest>
{
    public Task HandleAsync(DeleteItemRequest request, CancellationToken cancellationToken)
        => Task.CompletedTask;
}

// ── No handler registered (used to verify negative DI lookup) ────────────────

public sealed record UnhandledRequest : IRequest;

// ── Result request returning null ─────────────────────────────────────────────

public sealed record GetNullRequest : IRequest<GreetingResult?>;

public sealed class GetNullHandler : IRequestHandler<GetNullRequest, GreetingResult?>
{
    public Task<GreetingResult?> HandleAsync(GetNullRequest request, CancellationToken cancellationToken)
        => Task.FromResult<GreetingResult?>(null);
}

// ── Result request ────────────────────────────────────────────────────────────

[Description("Greets a person by name.")]
public sealed record GetGreetingRequest(string Name) : IRequest<GreetingResult>;

public sealed record GreetingResult(string Message);

public sealed class GetGreetingHandler : IRequestHandler<GetGreetingRequest, GreetingResult>
{
    public Task<GreetingResult> HandleAsync(GetGreetingRequest request, CancellationToken cancellationToken)
        => Task.FromResult(new GreetingResult($"Hello, {request.Name}!"));
}

// ── Enum serialization ────────────────────────────────────────────────────────

public enum Color { Red, Green, Blue }

public sealed record GetColorRequest(Color Color) : IRequest<ColorResult>;

public sealed record ColorResult(Color Color);

public sealed class GetColorHandler : IRequestHandler<GetColorRequest, ColorResult>
{
    public Task<ColorResult> HandleAsync(GetColorRequest request, CancellationToken cancellationToken)
        => Task.FromResult(new ColorResult(request.Color));
}

// ── Attributes applied to the endpoint ────────────────────────────────────────

[Authorize]
public sealed record GetSecretRequest : IRequest<string>;

public sealed class GetSecretHandler : IRequestHandler<GetSecretRequest, string>
{
    public Task<string> HandleAsync(GetSecretRequest request, CancellationToken cancellationToken) => Task.FromResult("secret");
}

[AllowAnonymous]
public sealed record GetPublicRequest : IRequest<string>;

public sealed class GetPublicHandler : IRequestHandler<GetPublicRequest, string>
{
    public Task<string> HandleAsync(GetPublicRequest request, CancellationToken cancellationToken) => Task.FromResult("public");
}

[Authorize]
public abstract record AdminRequest;

public sealed record ResetRequest : AdminRequest, IRequest;

public sealed class ResetHandler : IRequestHandler<ResetRequest>
{
    public Task HandleAsync(ResetRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
}
