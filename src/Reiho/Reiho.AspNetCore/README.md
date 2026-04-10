# Reiho.AspNetCore

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
// Scans the calling assembly
builder.Services.AddRequestHandlers();

// Explicit assembly — recommended when calling from a library or if auto-detection fails
builder.Services.AddRequestHandlers(typeof(Program).Assembly);
```

### Map endpoints

```csharp
// Maps all requests found in the calling assembly
app.MapRequests();

// Explicit assembly
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

---

## Embedded SPA

Serves a single-page application bundled as embedded resources, with automatic base path injection and aggressive caching for static assets.

### Project setup

Add to your `.csproj`:

```xml
<EmbeddedResource Include="Frontend\dist\**\*" />
<GenerateEmbeddedFilesManifest>true</GenerateEmbeddedFilesManifest>
```

Place `__BASE_PATH__` somewhere in your `index.html` — it is replaced at runtime with the actual mount path so the SPA router works correctly regardless of where it is mounted:

```html
<script>window.__BASE_PATH__ = '__BASE_PATH__'</script>
```

### Mount

```csharp
app.MapEmbeddedSpa(
    path:     "/ui",
    assembly: Assembly.GetExecutingAssembly(),
    rootPath: "Frontend/dist");
```

`rootPath` defaults to `"SPA/dist"` if not specified.

Returns a `RouteGroupBuilder` — chain standard ASP.NET Core conventions directly:

```csharp
app.MapEmbeddedSpa("/ui", Assembly.GetExecutingAssembly(), "Frontend/dist")
   .RequireAuthorization();
```

### Custom base path placeholder

```csharp
app.MapEmbeddedSpa("/ui", Assembly.GetExecutingAssembly(), "Frontend/dist",
    basePathPlaceholder: "%%BASE%%");
```

### Caching behaviour

| File | Cache-Control |
|---|---|
| `index.html` | `no-store` |
| All other assets | `public, max-age=31536000, immutable` |

Files are read once and cached in memory. Multiple SPAs mounted at different paths are cached independently.

---

## Full example

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRequestHandlers();

var app = builder.Build();

app.MapGroup("/api").MapRequests(typeof(Program).Assembly);

app.MapEmbeddedSpa("/ui", Assembly.GetExecutingAssembly(), "Frontend/dist")
   .RequireAuthorization();

app.Run();
```

---

## Requirements

- .NET 8 or later
- ASP.NET Core

---

## License

MIT
