using System.ComponentModel.DataAnnotations;
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

// ── Enum serialization ────────────────────────────────────────────────────────

public enum Color { Red, Green, Blue }

public sealed record GetColorRequest(Color Color) : IRequest<ColorResult>;

public sealed record ColorResult(Color Color);

public sealed class GetColorHandler : IRequestHandler<GetColorRequest, ColorResult>
{
    public Task<ColorResult> HandleAsync(GetColorRequest request, CancellationToken cancellationToken)
        => Task.FromResult(new ColorResult(request.Color));
}

// ── Validation ────────────────────────────────────────────────────────────────

public sealed record CreateUserRequest([Required, StringLength(5)] string? Name, [Range(1, 120)] int Age)
    : IRequest<string>, IValidatableObject
{
    [EmailAddress]
    public string? Email { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Name == "root")
            yield return new ValidationResult("The name is reserved.", [nameof(Name)]);
    }
}

public sealed class CreateUserHandler : IRequestHandler<CreateUserRequest, string>
{
    public static int CallCount;

    public Task<string> HandleAsync(CreateUserRequest request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref CallCount);
        return Task.FromResult($"Created {request.Name}.");
    }
}

// ── Errors thrown by a handler ────────────────────────────────────────────────

public sealed record FindUserRequest(int Id) : IRequest<string>;

public sealed class FindUserHandler : IRequestHandler<FindUserRequest, string>
{
    public Task<string> HandleAsync(FindUserRequest request, CancellationToken cancellationToken) => request.Id switch
    {
        1 => Task.FromResult("Ada"),
        2 => throw RequestException.Conflict("User 2 is being merged."),
        3 => throw RequestException.Invalid(nameof(request.Id), "Ids below 10 are reserved."),
        4 => throw new InvalidOperationException("Not a request error."),
        _ => throw RequestException.NotFound($"User {request.Id} does not exist."),
    };
}
