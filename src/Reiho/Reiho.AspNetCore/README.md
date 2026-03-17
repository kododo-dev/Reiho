# Reiho.AspNetCore

A lightweight and structured request/handler abstraction layer for ASP.NET Core Minimal APIs, designed for scalability, maintainability, and clean architecture.

---

## Overview

**Reiho.AspNetCore** introduces a consistent pattern for handling application logic using request and handler abstractions. It reduces boilerplate while maintaining explicit structure, making it suitable for both small services and large-scale systems.

The library is optimized for:

* Clean Architecture
* Vertical Slice Architecture
* Minimal API-based services

---

## Key Features

* Structured request/handler pattern (`IRequest`, `IRequestHandler`)
* Automatic dependency injection registration
* Convention-based endpoint mapping
* Seamless integration with ASP.NET Core Minimal APIs
* Minimal overhead and zero unnecessary abstractions

---

## Installation

Install the package via NuGet:

```bash id="y3d7k2"
dotnet add package Kododo.Reiho.AspNetCore
```

Or via Package Manager:

```powershell id="g1s8lp"
Install-Package Kododo.Reiho.AspNetCore
```

---

## Quick Start

### Define a Request

```csharp id="qj2k1v"
public sealed class CreateUserRequest : IRequest
{
    public string Name { get; init; } = default!;
}
```

Or with a response:

```csharp id="z0pl8x"
public sealed class GetUserRequest : IRequest<UserDto>
{
    public int Id { get; init; }
}
```

---

### Implement a Handler

```csharp id="t4a9mn"
public sealed class CreateUserHandler : IRequestHandler<CreateUserRequest>
{
    public Task HandleAsync(CreateUserRequest request, CancellationToken ct)
    {
        // Business logic
        return Task.CompletedTask;
    }
}
```

With a response:

```csharp id="k9w2rb"
public sealed class GetUserHandler : IRequestHandler<GetUserRequest, UserDto>
{
    public Task<UserDto> HandleAsync(GetUserRequest request, CancellationToken ct)
    {
        return Task.FromResult(new UserDto
        {
            Id = request.Id
        });
    }
}
```

---

### Register Services

```csharp id="a7m3zs"
builder.Services.AddRequestHandlers();
```

Or limit scanning scope:

```csharp id="h2c9xp"
builder.Services.AddRequestHandlers(typeof(Program).Assembly);
```

---

### Map Endpoints

```csharp id="d8l1vf"
app.MapRequests();
```

With base path:

```csharp id="p6x4qe"
app.MapRequests("/api");
```

---

## Runtime Behavior

### Endpoint Generation

Endpoints are generated automatically using conventions:

```
POST /CreateUserRequest
POST /GetUserRequest
```

---

### Request Processing

* Requests are resolved from the HTTP body (JSON)
* If no body is provided:

    * A parameterless constructor is used (if available)
    * Otherwise, request is rejected (`400 Bad Request`)

---

### Response Handling

| Request Type        | Result                |
| ------------------- | --------------------- |
| `IRequest`          | `204 No Content`      |
| `IRequest<TResult>` | `200 OK` with payload |

---

## Design Principles

Reiho is built around the following principles:

* **Explicit over implicit** – clear separation of concerns
* **Convention over configuration** – minimal setup required
* **Single responsibility** – each handler handles one use case
* **Scalability** – suitable for modular and distributed systems

---

## Use Cases

* Microservices
* Modular monoliths
* Internal APIs
* Rapid prototyping with clean boundaries

---

## Example Application

```csharp id="s4u8wr"
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRequestHandlers();

var app = builder.Build();

app.MapRequests("/api");

app.Run();
```

---

## Versioning & Compatibility

* Designed for modern ASP.NET Core applications
* Supports .NET 8+

---

## License

MIT
