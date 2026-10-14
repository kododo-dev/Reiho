using System.ComponentModel;
using Kododo.Reiho.AspNetCore.API;
using Kododo.Reiho.AspNetCore.Tests.API;

namespace Kododo.Reiho.AspNetCore.Tests.Mcp;

[McpTool(ReadOnly = true), Description("Greets a person by name.")]
public sealed record GreetPersonRequest([property: Description("Who to greet")] string Name, Color Mood = Color.Green)
    : IRequest<GreetingResult>;

public sealed class GreetPersonHandler : IRequestHandler<GreetPersonRequest, GreetingResult>
{
    public Task<GreetingResult> HandleAsync(GreetPersonRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new GreetingResult($"Hello, {request.Name} ({request.Mood})!"));
}

[McpTool(Name = "forget_person", Idempotent = true), Description("Forgets a person.")]
public sealed record ForgetPersonRequest(string Name) : IRequest;

public sealed class ForgetPersonHandler : IRequestHandler<ForgetPersonRequest>
{
    public static readonly List<string> Forgotten = [];

    public Task HandleAsync(ForgetPersonRequest request, CancellationToken cancellationToken)
    {
        lock (Forgotten)
            Forgotten.Add(request.Name);
        return Task.CompletedTask;
    }
}

// Not marked, so offered only when included.
public sealed record CountPeopleRequest : IRequest<int>;

public sealed class CountPeopleHandler : IRequestHandler<CountPeopleRequest, int>
{
    public Task<int> HandleAsync(CountPeopleRequest request, CancellationToken cancellationToken) => Task.FromResult(42);
}
