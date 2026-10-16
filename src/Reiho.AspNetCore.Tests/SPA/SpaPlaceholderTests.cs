using System.Net;
using System.Reflection;
using Kododo.Reiho.AspNetCore.SPA;
using Microsoft.AspNetCore.TestHost;
using Xunit;

namespace Kododo.Reiho.AspNetCore.Tests.SPA;

// TestSpa/index.html carries __REQUEST_ID__ and __IMPORT_MAP__ next to the base path.
public sealed class SpaPlaceholderTests : IAsyncLifetime
{
    private IHost? _host;
    private HttpClient? _client;
    private int _unusedPlaceholderCalls;

    private static readonly Assembly ThisAssembly = Assembly.GetExecutingAssembly();
    private const string GroupPrefix = "/ui";

    public async Task InitializeAsync()
    {
        _host = await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services => services.AddRouting());
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGroup(GroupPrefix).MapEmbeddedSpa(ThisAssembly, o =>
                        {
                            o.RootPath = "SPA/TestSpa";
                            o.Placeholder("__REQUEST_ID__", context => context.Request.Headers["X-Request-Id"].ToString())
                             .Placeholder("__IMPORT_MAP__", async context =>
                             {
                                 await Task.Yield();
                                 return $$$"""{"imports":{"react":"{{{context.Request.PathBase}}}/vendor/react.js"}}""";
                             })
                             .Placeholder("__NOT_IN_INDEX__", _ =>
                             {
                                 Interlocked.Increment(ref _unusedPlaceholderCalls);
                                 return "";
                             });
                        });
                    });
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

    [Fact]
    public async Task Index_html_has_placeholders_replaced_per_request()
    {
        var first = await GetIndex("first");
        var second = await GetIndex("second");

        Assert.Contains("""<meta name="request-id" content="first">""", first);
        Assert.Contains("""<meta name="request-id" content="second">""", second);
    }

    [Fact]
    public async Task Index_html_has_async_placeholder_replaced()
    {
        var body = await GetIndex("any");

        Assert.Contains("""<script type="importmap">{"imports":{"react":"/vendor/react.js"}}</script>""", body);
        Assert.DoesNotContain("__IMPORT_MAP__", body);
    }

    [Fact]
    public async Task Index_html_still_has_base_path_replaced()
    {
        var body = await GetIndex("any");

        Assert.Contains($"""<base href="{GroupPrefix}/">""", body);
    }

    [Fact]
    public async Task Placeholder_missing_from_index_html_is_not_computed()
    {
        await GetIndex("any");

        Assert.Equal(0, _unusedPlaceholderCalls);
    }

    [Fact]
    public async Task Static_assets_are_served_unchanged()
    {
        var response = await _client!.GetAsync($"{GroupPrefix}/assets/app.js");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("public, max-age=31536000, immutable", response.Headers.CacheControl?.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Placeholder_must_not_be_empty(string? placeholder)
    {
        var options = new EmbeddedSpaOptions();

        Assert.ThrowsAny<ArgumentException>(() => options.Placeholder(placeholder!, _ => ""));
    }

    private async Task<string> GetIndex(string requestId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, GroupPrefix);
        request.Headers.Add("X-Request-Id", requestId);
        var response = await _client!.SendAsync(request);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        return await response.Content.ReadAsStringAsync();
    }
}
