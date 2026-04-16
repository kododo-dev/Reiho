using Kododo.Reiho.AspNetCore.API;

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

public sealed record GetGreetingRequest(string Name) : IRequest<GreetingResult>;

public sealed record GreetingResult(string Message);

public sealed class GetGreetingHandler : IRequestHandler<GetGreetingRequest, GreetingResult>
{
    public Task<GreetingResult> HandleAsync(GetGreetingRequest request, CancellationToken cancellationToken)
        => Task.FromResult(new GreetingResult($"Hello, {request.Name}!"));
}
