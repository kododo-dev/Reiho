using System.Net;
using System.Reflection;
using Kododo.Reiho.AspNetCore.SPA;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Kododo.Reiho.AspNetCore.Tests.SPA;

// The SPA is mounted under /ui via MapGroup — this is the only supported pattern.
// Accessing /ui (no trailing slash) → serves index.html.
// Accessing /ui/assets/app.js     → serves the static asset.
// The library uses String.Replace on the raw HTML, so the placeholder must appear
// exactly once in the template (not as part of a JS variable name) to avoid mangling.
// TestSpa/index.html uses <base href="__BASE_PATH__"> for this reason.

public sealed class SpaServingTests : IAsyncLifetime
{
    private IHost? _host;
    private HttpClient? _client;

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
                        // MapGroup provides the mount path — MapEmbeddedSpa has no path param.
                        endpoints.MapGroup(GroupPrefix)
                                 .MapEmbeddedSpa(ThisAssembly, rootPath: "SPA/TestSpa");
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

    // ── index.html ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Group_root_returns_200()
    {
        var response = await _client!.GetAsync(GroupPrefix);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Group_root_returns_html_content_type()
    {
        var response = await _client!.GetAsync(GroupPrefix);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Index_html_has_no_store_cache_control()
    {
        var response = await _client!.GetAsync(GroupPrefix);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Index_html_has_base_path_placeholder_replaced()
    {
        var response = await _client!.GetAsync(GroupPrefix);
        var body = await response.Content.ReadAsStringAsync();

        // The raw placeholder must be gone…
        Assert.DoesNotContain("__BASE_PATH__", body);
        // …replaced by the calculated base path for the /ui group.
        Assert.Contains($"{GroupPrefix}/", body);
    }

    // ── Static assets ────────────────────────────────────────────────────────

    [Fact]
    public async Task Static_asset_returns_200()
    {
        var response = await _client!.GetAsync($"{GroupPrefix}/assets/app.js");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Static_asset_has_immutable_cache_control()
    {
        var response = await _client!.GetAsync($"{GroupPrefix}/assets/app.js");
        Assert.Equal("public, max-age=31536000, immutable", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Static_asset_returns_correct_content_type()
    {
        var response = await _client!.GetAsync($"{GroupPrefix}/assets/app.js");
        Assert.Equal("text/javascript", response.Content.Headers.ContentType?.MediaType);
    }

    // ── Not found ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Non_existent_file_returns_404()
    {
        var response = await _client!.GetAsync($"{GroupPrefix}/does-not-exist.png");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Unknown file extension ───────────────────────────────────────────────

    [Fact]
    public async Task File_with_unknown_extension_returns_200()
    {
        var response = await _client!.GetAsync($"{GroupPrefix}/assets/data.xyz");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task File_with_unknown_extension_returns_octet_stream_content_type()
    {
        var response = await _client!.GetAsync($"{GroupPrefix}/assets/data.xyz");
        Assert.Equal("application/octet-stream", response.Content.Headers.ContentType?.MediaType);
    }

    // ── Caching ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Second_request_for_asset_is_served_from_cache()
    {
        await _client!.GetAsync($"{GroupPrefix}/assets/app.js");
        var response = await _client!.GetAsync($"{GroupPrefix}/assets/app.js");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Second_request_for_asset_has_immutable_cache_control()
    {
        await _client!.GetAsync($"{GroupPrefix}/assets/app.js");
        var response = await _client!.GetAsync($"{GroupPrefix}/assets/app.js");
        Assert.Equal("public, max-age=31536000, immutable", response.Headers.CacheControl?.ToString());
    }

    // ── Known edge cases (documenting current behaviour) ─────────────────────

    [Fact]
    public async Task Trailing_slash_after_group_prefix_returns_404()
    {
        // "/ui/" → filePath = "/" → not empty, not "index.html" → file not found.
        // Clients should navigate to "/ui" (no trailing slash).
        var response = await _client!.GetAsync($"{GroupPrefix}/");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
