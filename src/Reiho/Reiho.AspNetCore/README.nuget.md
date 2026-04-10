# Reiho.AspNetCore

Lightweight request/handler abstraction for ASP.NET Core Minimal APIs, plus a helper for serving embedded SPAs.

## Install

```bash
dotnet add package Kododo.Reiho.AspNetCore
```

## Request / Handler

```csharp
// Define
public sealed record GetUser(int Id) : IRequest<UserDto>;

// Handle
public sealed class GetUserHandler : IRequestHandler<GetUser, UserDto>
{
    public Task<UserDto> HandleAsync(GetUser request, CancellationToken ct)
        => Task.FromResult(new UserDto { Id = request.Id });
}

// Register & map
builder.Services.AddRequestHandlers();
app.MapGroup("/api").MapRequests(typeof(Program).Assembly);
// → POST /api/GetUser
```

Requests without a return value use `IRequest` / `IRequestHandler<T>` and return `204 No Content`.

## Request body behaviour

| Situation | Result |
|---|---|
| Body present | Deserialized from JSON |
| No body, parameterless constructor exists | Empty instance created |
| No body, no parameterless constructor | `400 Bad Request` |

## Embedded SPA

Serve a bundled SPA from embedded resources with automatic base path injection:

```csharp
app.MapEmbeddedSpa("/ui", Assembly.GetExecutingAssembly(), "Frontend/dist")
   .RequireAuthorization();
```

Add to your `.csproj`:

```xml
<EmbeddedResource Include="Frontend\dist\**\*" />
<GenerateEmbeddedFilesManifest>true</GenerateEmbeddedFilesManifest>
```

Place `__BASE_PATH__` in your `index.html` — replaced at runtime with the actual mount path. `rootPath` defaults to `"SPA/dist"`.

## Links

- Source: https://github.com/kododo-dev/Reiho
