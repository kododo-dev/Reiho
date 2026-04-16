using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Kododo.Reiho.AspNetCore.API;
using Xunit;

namespace Kododo.Reiho.AspNetCore.Tests.API;

public sealed class RequestHandlingTests : IAsyncLifetime
{
    private IHost? _host;
    private HttpClient? _client;

    public async Task InitializeAsync()
    {
        _host = await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddRequestHandlers(Assembly.GetExecutingAssembly());
                    services.AddRouting();
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapRequests(Assembly.GetExecutingAssembly());
                    });
                });
            })
            .StartAsync();

        _client = _host.GetTestClient();
        PingHandler.CallCount = 0;
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
    }

    // ── IRequest (no result) ──────────────────────────────────────────────────

    [Fact]
    public async Task Void_request_with_no_body_and_parameterless_ctor_returns_204()
    {
        var response = await _client!.PostAsync("/PingRequest", content: null);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Void_request_with_no_body_invokes_handler()
    {
        await _client!.PostAsync("/PingRequest", content: null);
        Assert.Equal(1, PingHandler.CallCount);
    }

    [Fact]
    public async Task Void_request_with_valid_json_body_returns_204()
    {
        var json = new StringContent("{}", Encoding.UTF8, "application/json");
        var response = await _client!.PostAsync("/PingRequest", json);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Void_request_with_no_body_and_no_parameterless_ctor_returns_400()
    {
        var response = await _client!.PostAsync("/DeleteItemRequest", content: null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Void_request_with_valid_body_returns_204()
    {
        // System.Text.Json default options are case-sensitive → PascalCase keys required
        var json = new StringContent("""{"Id":42}""", Encoding.UTF8, "application/json");
        var response = await _client!.PostAsync("/DeleteItemRequest", json);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Void_request_with_invalid_json_returns_400()
    {
        var badJson = new StringContent("not-json", Encoding.UTF8, "application/json");
        var response = await _client!.PostAsync("/DeleteItemRequest", badJson);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── IRequest<TResult> ────────────────────────────────────────────────────

    [Fact]
    public async Task Result_request_with_valid_body_returns_200()
    {
        var json = new StringContent("""{"Name":"World"}""", Encoding.UTF8, "application/json");
        var response = await _client!.PostAsync("/GetGreetingRequest", json);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Result_request_returns_json_content_type()
    {
        var json = new StringContent("""{"Name":"World"}""", Encoding.UTF8, "application/json");
        var response = await _client!.PostAsync("/GetGreetingRequest", json);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Result_request_returns_correct_json_body()
    {
        var json = new StringContent("""{"Name":"World"}""", Encoding.UTF8, "application/json");
        var response = await _client!.PostAsync("/GetGreetingRequest", json);

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        // System.Text.Json serialises with PascalCase property names by default
        Assert.Equal("Hello, World!", doc.RootElement.GetProperty("Message").GetString());
    }

    [Fact]
    public async Task Result_request_with_no_body_and_no_ctor_returns_400()
    {
        var response = await _client!.PostAsync("/GetGreetingRequest", content: null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Result_request_with_invalid_json_returns_400()
    {
        var badJson = new StringContent("not-json", Encoding.UTF8, "application/json");
        var response = await _client!.PostAsync("/GetGreetingRequest", badJson);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Null result ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Result_request_handler_returning_null_returns_200()
    {
        var json = new StringContent("{}", Encoding.UTF8, "application/json");
        var response = await _client!.PostAsync("/GetNullRequest", json);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Result_request_handler_returning_null_serializes_json_null()
    {
        var json = new StringContent("{}", Encoding.UTF8, "application/json");
        var response = await _client!.PostAsync("/GetNullRequest", json);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("null", body);
    }

    // ── Endpoint discovery ───────────────────────────────────────────────────

    [Fact]
    public async Task Unknown_endpoint_returns_404()
    {
        var response = await _client!.PostAsync("/NonExistentRequest", content: null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
