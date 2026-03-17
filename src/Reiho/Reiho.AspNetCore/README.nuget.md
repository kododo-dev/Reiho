# Reiho.AspNetCore

Lightweight request/handler abstraction for ASP.NET Core Minimal APIs.

---

## Install

```bash
dotnet add package Kododo.Reiho.AspNetCore
```

---

## Quick Example

### Define request

```csharp
public sealed class GetUserRequest : IRequest<UserDto>
{
    public int Id { get; init; }
}
```

### Create handler

```csharp
public sealed class GetUserHandler : IRequestHandler<GetUserRequest, UserDto>
{
    public Task<UserDto> HandleAsync(GetUserRequest request, CancellationToken ct)
    {
        return Task.FromResult(new UserDto { Id = request.Id });
    }
}
```

### Setup

```csharp
builder.Services.AddRequestHandlers();
app.MapRequests("/api");
```

---

## How it works

* Requests are mapped automatically to HTTP endpoints
* Handlers are discovered via assembly scanning
* Endpoints use `POST` by default

Example:

```
POST /GetUserRequest
```

---

## Response behavior

* `IRequest` → `204 No Content`
* `IRequest<TResult>` → `200 OK`

---

## Links

* Source code: https://github.com/kododo-dev/Reiho
