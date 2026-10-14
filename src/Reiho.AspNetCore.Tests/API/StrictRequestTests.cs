using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Kododo.Reiho.AspNetCore.API;
using Microsoft.AspNetCore.TestHost;
using Xunit;

namespace Kododo.Reiho.AspNetCore.Tests.API;

public sealed class StrictRequestTests : IAsyncLifetime
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
                    app.UseEndpoints(endpoints => endpoints.MapRequests(Assembly.GetExecutingAssembly()));
                });
            })
            .StartAsync();
        _client = _host.GetTestClient();
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

    private const string Valid = """{"customer":"Ada","address":{"street":"Main 1","city":"Gdynia"},"lines":[{"quantity":2,"product":"tea"}]}""";

    private async Task<(HttpStatusCode Status, string Body)> Post(string json)
    {
        var response = await _client!.PostAsync("/PlaceOrderRequest", new StringContent(json, Encoding.UTF8, "application/json"));
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static Dictionary<string, string[]> Errors(string body) =>
        JsonDocument.Parse(body).RootElement.GetProperty("errors").Deserialize<Dictionary<string, string[]>>()!;

    [Fact]
    public async Task Valid_order_reaches_the_handler()
    {
        Assert.Equal((HttpStatusCode.OK, "1"), await Post(Valid));
    }

    [Fact]
    public async Task Unknown_field_is_rejected_and_named()
    {
        var (status, body) = await Post(Valid.Replace("\"customer\"", "\"costumer\":\"x\",\"customer\""));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(["The request has no such field."], Errors(body)["costumer"]);
    }

    [Fact]
    public async Task Unknown_nested_field_is_named_with_its_path()
    {
        var (_, body) = await Post(Valid.Replace("\"city\"", "\"zip\":\"81-300\",\"city\""));

        Assert.Equal(["The request has no such field."], Errors(body)["address.zip"]);
    }

    [Fact]
    public async Task Null_or_missing_non_nullable_reference_is_required()
    {
        var (status, body) = await Post("""{"customer":null,"lines":[]}""");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        var errors = Errors(body);
        Assert.Equal(["The customer field is required."], errors["customer"]);
        Assert.Equal(["The address field is required."], errors["address"]);
        Assert.False(errors.ContainsKey("note"));
    }

    [Fact]
    public async Task Empty_string_is_a_value()
    {
        Assert.Equal(HttpStatusCode.OK, (await Post(Valid.Replace("\"Ada\"", "\"\""))).Status);
    }

    [Fact]
    public async Task Nested_objects_and_collections_are_checked_with_their_paths()
    {
        var (status, body) = await Post("""
            {"customer":"Ada","address":{"street":null,"city":null},
             "lines":[{"quantity":2,"product":"gift"},{"quantity":0,"product":"tea"}]}
            """);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        var errors = Errors(body);
        Assert.Contains("address.street", errors.Keys);
        Assert.Contains("address.city", errors.Keys);
        Assert.Contains("lines[1].quantity", errors.Keys);
        Assert.Equal(["Only one gift per order."], errors["lines[0].quantity"]);
    }
}
