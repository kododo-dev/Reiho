using System.Text.Json.Serialization;
using Kododo.Reiho.AspNetCore.API;
using Kododo.Reiho.AspNetCore.Mcp;
using Kododo.Reiho.Sample;
using Microsoft.AspNetCore.Antiforgery;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<OrderStore>();
builder.Services.AddRequestHandlers(typeof(Program).Assembly);
builder.Services.AddRequestFilter<TimingFilter>();

builder.Services.AddProblemDetails();
builder.Services.AddAntiforgery();
builder.Services.AddOpenApi();
// Document enums as strings, as Reiho writes them.
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithRequests(typeof(Program).Assembly);

builder.Logging.AddFilter(ReihoTelemetry.LogCategory, LogLevel.Debug);

var app = builder.Build();

app.UseAntiforgery();

app.MapOpenApi();
app.MapGroup("/api").MapRequests(typeof(Program).Assembly).WithTags("Orders");
// A real app protects this endpoint: a tool call does not go through the requests' HTTP endpoints.
app.MapMcp("/mcp");

// The token a form post to ImportOrders needs; a page or SPA would fetch it the same way.
app.MapGet("/antiforgery", (IAntiforgery antiforgery, HttpContext context) => antiforgery.GetAndStoreTokens(context).RequestToken);

app.Run();
