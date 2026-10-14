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
builder.Services.AddRequestHandlers(typeof(Program).Assembly);
app.MapGroup("/api").MapRequests(typeof(Program).Assembly);
// → POST /api/GetUser
```

Requests without a return value use `IRequest` / `IRequestHandler<T>` and return `204 No Content`.

## Validation and errors

Data annotations on the request, also on positional record parameters, are checked before the handler runs; a request that fails gets `400` with the errors per field as problem details (RFC 9457). A handler ends a request with an error the caller can act on by throwing `RequestException`:

```csharp
public sealed record CreateUser([Required, StringLength(50)] string Name) : IRequest<UserDto>;

throw RequestException.NotFound($"User {request.Id} does not exist.");       // 404
throw RequestException.Invalid(nameof(request.Name), "The name is taken.");   // 400, errors.name
```

`Conflict` (409) and `Forbidden` (403) work the same way. With `AddProblemDetails()`, problems go through the app's `IProblemDetailsService`.

## Attributes

Attributes on the request apply to its endpoint, as on a controller action, e.g. `[Authorize(Roles = "Admin")]`, `[AllowAnonymous]` or `[EnableRateLimiting]`.

## Files

`IFormFile` properties make a request take `multipart/form-data`, with an antiforgery token required as for minimal API forms (`[RequireAntiforgeryToken(false)]` opts out). A handler returns a file with `FileResponse`:

```csharp
public sealed record UploadAvatar(int UserId) : IRequest { [Required] public IFormFile? Avatar { get; init; } }
public sealed record ExportOrders : IRequest<FileResponse>;

return FileResponse.FromText(csv, "text/csv", "orders.csv");
```

## Request filters

`IRequestFilter` runs around every handler, over HTTP and MCP, once the request is validated, and sees the request object:

```csharp
builder.Services.AddRequestFilter<AuditFilter>();
```

## OpenAPI

Endpoints describe themselves for ApiExplorer, so `Microsoft.AspNetCore.OpenApi`, Swashbuckle and Scalar document them: request body, result type, problem responses, the request's `[Description]` as summary, and an operation id from the full route (`/api/GetUser` → `Api_GetUser`).

## MCP

[Kododo.Reiho.AspNetCore.Mcp](https://www.nuget.org/packages/Kododo.Reiho.AspNetCore.Mcp) offers requests as tools to AI agents.

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

- Source and full documentation: https://github.com/kododo-dev/Reiho
