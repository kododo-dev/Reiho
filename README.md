# Reiho.AspNetCore

[![CI](https://github.com/kododo-dev/Reiho/actions/workflows/ci.yml/badge.svg)](https://github.com/kododo-dev/Reiho/actions/workflows/ci.yml)

Lightweight request/handler abstraction for ASP.NET Core Minimal APIs, plus a helper for serving embedded SPAs.

---

## Installation

```bash
dotnet add package Kododo.Reiho.AspNetCore
```

---

## Request / Handler

### Define a request

Without a return value:

```csharp
public sealed record DeleteUser(int Id) : IRequest;
```

With a return value:

```csharp
public sealed record GetUser(int Id) : IRequest<UserDto>;
```

### Implement a handler

```csharp
public sealed class DeleteUserHandler : IRequestHandler<DeleteUser>
{
    public Task HandleAsync(DeleteUser request, CancellationToken ct)
    {
        // ...
        return Task.CompletedTask;
    }
}

public sealed class GetUserHandler : IRequestHandler<GetUser, UserDto>
{
    public Task<UserDto> HandleAsync(GetUser request, CancellationToken ct)
    {
        return Task.FromResult(new UserDto { Id = request.Id });
    }
}
```

### Register handlers

```csharp
// Scans the given assembly for handlers
builder.Services.AddRequestHandlers(typeof(Program).Assembly);
```

### Map endpoints

```csharp
// Maps all requests found in the given assembly
app.MapRequests(typeof(Program).Assembly);

// With a base path — use MapGroup
app.MapGroup("/api").MapRequests(typeof(Program).Assembly);
```

---

## Conventions

Each request type is mapped to a `POST` endpoint named after the type:

```
POST /GetUser
POST /DeleteUser
```

**Request body:**

| Situation | Behaviour |
|---|---|
| Body present | Deserialized from JSON |
| No body, parameterless constructor exists | Empty instance created |
| No body, no parameterless constructor | `400 Bad Request` |

**Response:**

| Request type | Status | Body |
|---|---|---|
| `IRequest` | `204 No Content` | — |
| `IRequest<TResult>` | `200 OK` | JSON |

**Attributes:** attributes on the request type apply to its endpoint, as on a controller action, including those inherited from a base request:

```csharp
[Authorize(Roles = "Admin")]
public sealed record DeleteUser(int Id) : IRequest;

[AllowAnonymous]   // open, even in a group with RequireAuthorization()
public sealed record GetStatus : IRequest<Status>;
```

This works for any endpoint metadata attribute, e.g. `[EnableRateLimiting]` or `[Tags]`. An `[Authorize]` request needs `UseAuthorization()` in the pipeline; without it, ASP.NET Core rejects calls to that endpoint rather than serving them unprotected.

---

## OpenAPI

Endpoints describe themselves for ApiExplorer, so `Microsoft.AspNetCore.OpenApi`, Swashbuckle and Scalar document them without extra setup:

| | |
|---|---|
| Operation id | the full route (`/api/GetUser` → `Api_GetUser`) |
| Summary | the request's `[Description]` |
| Request body | the request as JSON; optional when it has a parameterless constructor |
| Responses | `200` with the result type, or `204` for an `IRequest`; `400` as plain text |

```csharp
[Description("Finds the orders of a customer, newest first.")]
public sealed record FindOrders([property: Description("Customer id, e.g. c-63")] string CustomerId) : IRequest<Order[]>;
```

```csharp
builder.Services.AddOpenApi();
app.MapOpenApi();
app.MapRequests(typeof(Program).Assembly);
```

Each endpoint is named after its full route, group prefix included (`/api/admin/GetUser` → `Api_Admin_GetUser`), which becomes the operation id that client generators (NSwag, Kiota, openapi-generator) build method names from. A route is unique, so requests of the same name mapped in different groups, e.g. by two libraries, do not clash.

Reiho writes enums as strings. The document's schemas follow the app's own JSON options, so add the same converter there to document them as strings:

```csharp
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
```

---

## Embedded SPA

Serves a single-page application bundled as embedded resources, with automatic base path injection and aggressive caching for static assets.

### Project setup

Add to your `.csproj`:

```xml
<PropertyGroup>
  <GenerateEmbeddedFilesManifest>true</GenerateEmbeddedFilesManifest>
</PropertyGroup>

<ItemGroup>
  <EmbeddedResource Include="Frontend\dist\**\*" />
  <PackageReference Include="Microsoft.Extensions.FileProviders.Embedded" Version="10.0.12" />
</ItemGroup>
```

`Microsoft.Extensions.FileProviders.Embedded` provides the build task that writes the manifest. Use a 10.x version on every target framework, net8.0 and net9.0 included. The .NET 10 SDK prunes 8.x and 9.x as part of the shared framework, which drops the task, so the manifest is missing and `MapEmbeddedSpa` fails at startup.

Place `__BASE_PATH__` somewhere in your `index.html` — it is replaced at runtime with the actual mount path so the SPA router works correctly regardless of where it is mounted:

```html
<script>window.__BASE_PATH__ = '__BASE_PATH__'</script>
```

### Mount

The mount path comes from `MapGroup`. `MapEmbeddedSpa` requires an assembly and accepts an optional `rootPath`:

```csharp
app.MapGroup("/ui").MapEmbeddedSpa(Assembly.GetExecutingAssembly(), "Frontend/dist");
```

`rootPath` defaults to `"SPA/dist"` if not specified. To add ASP.NET Core conventions such as authorization, apply them on the group builder before calling `MapEmbeddedSpa`:

```csharp
app.MapGroup("/ui")
   .RequireAuthorization()
   .MapEmbeddedSpa(Assembly.GetExecutingAssembly(), "Frontend/dist");
```

### Base path behind a reverse proxy

By default the base path is calculated from the incoming request. This breaks when the application runs behind a reverse proxy that strips a path prefix before forwarding — for example Caddy's `handle_path`.

Set the `ASPNETCORE_PATHBASE` environment variable to override the auto-calculated value:

```
ASPNETCORE_PATHBASE=/configway/demo/ui
```

The value must combine the prefix stripped by the proxy **and** the `MapGroup` path, so that asset URLs resolve correctly for the browser. With the example above and `MapGroup("/ui")`:

- public URL: `https://example.com/configway/demo/ui/`
- Caddy strips `/configway/demo`, passes `/ui/…` to the backend
- `ASPNETCORE_PATHBASE=/configway/demo/ui` → `<base href="/configway/demo/ui/">`
- browser resolves `assets/app.js` → `/configway/demo/ui/assets/app.js` → Caddy strips prefix → `/ui/assets/app.js` ✓

To apply the same value to ASP.NET Core's own routing and link generation, add this to `Program.cs`:

```csharp
app.UsePathBase(app.Configuration["PathBase"] ?? "");
```

### Custom base path placeholder

```csharp
app.MapGroup("/ui").MapEmbeddedSpa(
    Assembly.GetExecutingAssembly(),
    rootPath: "Frontend/dist",
    basePathPlaceholder: "%%BASE%%");
```

### Caching behaviour

| File | Cache-Control |
|---|---|
| `index.html` | `no-store` |
| All other assets | `public, max-age=31536000, immutable` |

Files are read once and cached in memory. Multiple SPAs mounted at different paths are cached independently.

---

## MCP tools (preview)

`Kododo.Reiho.AspNetCore.Mcp` offers requests as tools to AI agents over the [Model Context Protocol](https://modelcontextprotocol.io), next to their HTTP endpoints, with the official MCP C# SDK.

```bash
dotnet add package Kododo.Reiho.AspNetCore.Mcp
```

Mark the requests to offer with `[McpTool]` and describe them; agents choose and call tools from these descriptions:

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

| | |
|---|---|
| Tool name | the request's name in snake case (`find_orders`), or `[McpTool(Name = ...)]`; `o.Prefix` puts a prefix before every name |
| Input schema | the request's properties, with their `[Description]` |
| Result | the handler's result as JSON; `Done.` for an `IRequest` |
| Invalid arguments | an error result the agent can read and correct |
| Hints | `ReadOnly`, `Destructive`, `Idempotent`, `OpenWorld` on `[McpTool]` |

Requests without `[McpTool]` are not offered. A library that cannot reference the attribute's version of Reiho includes its requests with `o.Include<TRequest>(new McpToolAttribute { ReadOnly = true })`.

A tool call does not go through the request's HTTP endpoint, so endpoint filters and the endpoint's authorization do not apply: protect the MCP endpoint itself. `[Authorize]` on a request is honoured per tool after `AddAuthorizationFilters()`. On net8.0 and net9.0 the MCP SDK brings the 10.x `Microsoft.Extensions.*` packages; align the app's own `Microsoft.Extensions.Logging` with them if it references an older one.

---

## Full example

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRequestHandlers(typeof(Program).Assembly);

var app = builder.Build();

app.MapGroup("/api").MapRequests(typeof(Program).Assembly);

app.MapGroup("/ui")
   .RequireAuthorization()
   .MapEmbeddedSpa(Assembly.GetExecutingAssembly(), "Frontend/dist");

app.Run();
```

---

## Requirements

- .NET 8 or later
- ASP.NET Core

---

## License

MIT
