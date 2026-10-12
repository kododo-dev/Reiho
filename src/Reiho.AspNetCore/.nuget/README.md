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
app.MapGroup("/ui")
   .RequireAuthorization()
   .MapEmbeddedSpa(Assembly.GetExecutingAssembly(), "Frontend/dist");
```

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

Place `__BASE_PATH__` in your `index.html` — replaced at runtime with the actual mount path. `rootPath` defaults to `"SPA/dist"`.

When running behind a reverse proxy that strips a path prefix (e.g. Caddy's `handle_path`), set `ASPNETCORE_PATHBASE` to override the auto-calculated base path:

```
ASPNETCORE_PATHBASE=/configway/demo/ui
```

The value must be the full public prefix as seen by the browser — the proxy's stripped prefix plus the `MapGroup` path.

## Links

- Source: https://github.com/kododo-dev/Reiho
