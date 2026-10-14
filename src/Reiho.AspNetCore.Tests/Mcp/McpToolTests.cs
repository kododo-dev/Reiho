using System.Reflection;
using System.Text.Json;
using Kododo.Reiho.AspNetCore.API;
using Kododo.Reiho.AspNetCore.Mcp;
using Kododo.Reiho.AspNetCore.Tests.API;
using Microsoft.AspNetCore.TestHost;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Kododo.Reiho.AspNetCore.Tests.Mcp;

public sealed class McpToolTests
{
    private static async Task<(IHost Host, McpClient Client)> StartAsync(Action<ReihoMcpOptions>? configure = null)
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddRequestHandlers(Assembly.GetExecutingAssembly());
                    services.AddRouting();
                    services.AddMcpServer().WithHttpTransport().WithRequests(Assembly.GetExecutingAssembly(), configure);
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapMcp("/mcp"));
                });
            })
            .StartAsync();

        var http = host.GetTestClient();
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(http.BaseAddress!, "/mcp"), TransportMode = HttpTransportMode.StreamableHttp },
            http);
        return (host, await McpClient.CreateAsync(transport));
    }

    private static string TextOf(CallToolResult result) => Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;

    [Fact]
    public async Task OnlyMarkedRequests_AreTools()
    {
        var (host, client) = await StartAsync();
        using var _ = host;
        await using var __ = client;

        var tools = await client.ListToolsAsync();

        Assert.Equal(["forget_person", "greet_person_request"], tools.Select(t => t.Name).Order());
    }

    [Fact]
    public async Task Tool_DescribesTheRequest()
    {
        var (host, client) = await StartAsync();
        using var _ = host;
        await using var __ = client;

        var greet = (await client.ListToolsAsync()).Single(t => t.Name == "greet_person_request").ProtocolTool;

        Assert.Equal("Greets a person by name.", greet.Description);
        Assert.False(greet.InputSchema.TryGetProperty("description", out var repeated), $"The schema repeats the description: {repeated}");
        Assert.True(greet.Annotations?.ReadOnlyHint);
        Assert.Null(greet.Annotations?.DestructiveHint);
        var properties = greet.InputSchema.GetProperty("properties");
        Assert.Equal("Who to greet", properties.GetProperty("name").GetProperty("description").GetString());
        Assert.Equal(JsonValueKind.Object, properties.GetProperty("mood").ValueKind);
        Assert.Contains("name", greet.InputSchema.GetProperty("required").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task Call_RunsTheHandlerAndReturnsItsResultAsJson()
    {
        var (host, client) = await StartAsync();
        using var _ = host;
        await using var __ = client;

        var result = await client.CallToolAsync("greet_person_request", new Dictionary<string, object?> { ["name"] = "Ada", ["mood"] = "Blue" });

        Assert.NotEqual(true, result.IsError);
        Assert.Equal("Hello, Ada (Blue)!", JsonDocument.Parse(TextOf(result)).RootElement.GetProperty("message").GetString());
        Assert.DoesNotContain("\n", TextOf(result));
    }

    [Fact]
    public async Task Call_OfARequestWithoutResult_RunsTheHandler()
    {
        var (host, client) = await StartAsync();
        using var _ = host;
        await using var __ = client;

        var result = await client.CallToolAsync("forget_person", new Dictionary<string, object?> { ["name"] = "Bob" });

        Assert.Equal("Done.", TextOf(result));
        lock (ForgetPersonHandler.Forgotten)
            Assert.Contains("Bob", ForgetPersonHandler.Forgotten);
    }

    [Fact]
    public async Task Call_WithArgumentsThatDoNotFit_IsAnErrorTheAgentCanRead()
    {
        var (host, client) = await StartAsync();
        using var _ = host;
        await using var __ = client;

        var result = await client.CallToolAsync("greet_person_request", new Dictionary<string, object?> { ["name"] = "Ada", ["mood"] = "Purple" });

        Assert.True(result.IsError);
        Assert.StartsWith("The arguments do not fit the input schema", TextOf(result));
    }

    [Fact]
    public async Task Call_ThatFailsValidation_ListsTheErrors()
    {
        var (host, client) = await StartAsync(o => o.Include<CreateUserRequest>());
        using var _ = host;
        await using var __ = client;

        var result = await client.CallToolAsync("create_user_request", new Dictionary<string, object?> { ["name"] = "Alexandra", ["age"] = 0 });

        // The handler would answer "Created ...", not an error.
        Assert.True(result.IsError);
        var text = TextOf(result);
        Assert.StartsWith("The arguments are not valid:", text);
        Assert.Contains("\n- name: ", text);
        Assert.Contains("\n- age: ", text);
    }

    [Theory]
    [InlineData(9, "404 Not Found: User 9 does not exist.")]
    [InlineData(2, "409 Conflict: User 2 is being merged.")]
    [InlineData(3, "The arguments are not valid:\n- id: Ids below 10 are reserved.")]
    public async Task Call_WhoseHandlerThrowsARequestException_IsAnErrorTheAgentCanRead(int id, string expected)
    {
        var (host, client) = await StartAsync(o => o.Include<FindUserRequest>());
        using var _ = host;
        await using var __ = client;

        var result = await client.CallToolAsync("find_user_request", new Dictionary<string, object?> { ["id"] = id });

        Assert.True(result.IsError);
        Assert.Equal(expected, TextOf(result));
    }

    [Fact]
    public async Task Call_ReturningATextFile_GivesItsTextAsAResource()
    {
        var (host, client) = await StartAsync(o => o.Include<ExportGreetingsRequest>());
        using var _ = host;
        await using var __ = client;

        var result = await client.CallToolAsync("export_greetings_request", new Dictionary<string, object?> { ["format"] = "csv" });

        var resource = Assert.IsType<TextResourceContents>(Assert.IsType<EmbeddedResourceBlock>(Assert.Single(result.Content)).Resource);
        Assert.Equal("name\nAda", resource.Text);
        Assert.Equal("file:///greetings.csv", resource.Uri);
        Assert.StartsWith("text/csv", resource.MimeType);
    }

    [Fact]
    public async Task Call_ReturningABinaryFile_GivesItAsABlob()
    {
        var (host, client) = await StartAsync(o => o.Include<ExportGreetingsRequest>());
        using var _ = host;
        await using var __ = client;

        var result = await client.CallToolAsync("export_greetings_request", new Dictionary<string, object?> { ["format"] = "bin" });

        var resource = Assert.IsType<BlobResourceContents>(Assert.IsType<EmbeddedResourceBlock>(Assert.Single(result.Content)).Resource);
        Assert.Equal([1, 2, 3], resource.DecodedData.ToArray());
    }

    [Fact]
    public void RequestReceivingFiles_CannotBeATool()
    {
        var services = new ServiceCollection();

        var error = Assert.Throws<InvalidOperationException>(() => services.AddMcpServer().WithRequests(
            Assembly.GetExecutingAssembly(), o => o.Include<UploadAvatarRequest>()));

        Assert.Contains(nameof(UploadAvatarRequest), error.Message);
    }

    [Fact]
    public async Task Include_OffersAnUnmarkedRequest_WithAPrefix()
    {
        var (host, client) = await StartAsync(o =>
        {
            o.Prefix = "people_";
            o.Include<CountPeopleRequest>(new McpToolAttribute { Name = "count", ReadOnly = true });
        });
        using var _ = host;
        await using var __ = client;

        var tools = await client.ListToolsAsync();
        var result = await client.CallToolAsync("people_count");

        Assert.Contains("people_count", tools.Select(t => t.Name));
        Assert.Contains("people_forget_person", tools.Select(t => t.Name));
        Assert.Equal("42", TextOf(result));
    }

    [Fact]
    public void SameToolNameTwice_FailsAtStartup()
    {
        var services = new ServiceCollection();

        var error = Assert.Throws<InvalidOperationException>(() => services.AddMcpServer().WithRequests(
            Assembly.GetExecutingAssembly(), o => o.Include<CountPeopleRequest>(new McpToolAttribute { Name = "forget_person" })));

        Assert.Contains("forget_person", error.Message);
    }

    [Theory]
    [InlineData("GetEntries", "get_entries")]
    [InlineData("GetUISettings", "get_ui_settings")]
    [InlineData("Ping", "ping")]
    public void Names_AreSnakeCase(string type, string tool) => Assert.Equal(tool, RequestTool.SnakeCase(type));
}
