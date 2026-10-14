# Kododo.Reiho.AspNetCore.Mcp

Offers [Reiho](https://github.com/kododo-dev/Reiho) requests as tools to AI agents over the [Model Context Protocol](https://modelcontextprotocol.io), next to their HTTP endpoints. Built on the official MCP C# SDK.

```csharp
[McpTool(ReadOnly = true), Description("Finds the orders of a customer, newest first.")]
public sealed record FindOrders([property: Description("Customer id, e.g. c-63")] string CustomerId) : IRequest<Order[]>;
```

```csharp
builder.Services.AddRequestHandlers(typeof(Program).Assembly);
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithRequests(typeof(Program).Assembly);

app.MapMcp("/mcp").RequireAuthorization();
```

Only requests marked with `[McpTool]`, or included with `o.Include<TRequest>()`, become tools. A tool's arguments are the request's properties, and its result is the handler's result as JSON. A tool call does not go through the HTTP endpoints, so protect the MCP endpoint itself.

The request's validation, `RequestException`s and request filters apply to tool calls as to HTTP requests, and errors come back as error results the agent can read and act on. A `FileResponse` result is returned as an embedded resource; a request that receives files cannot be a tool.
