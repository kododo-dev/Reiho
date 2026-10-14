using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Kododo.Reiho.AspNetCore.API;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Kododo.Reiho.AspNetCore.Tests.API;

public sealed class RequestAttributesTests : IAsyncLifetime
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
                    services.AddAuthentication(HeaderAuthenticationHandler.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>(HeaderAuthenticationHandler.SchemeName, null);
                    services.AddAuthorization();
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGroup("/open").MapRequests(Assembly.GetExecutingAssembly());
                        endpoints.MapGroup("/closed").RequireAuthorization().MapRequests(Assembly.GetExecutingAssembly());
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

    private Task<HttpResponseMessage> Post(string path, bool signedIn)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, path);
        if (signedIn)
            message.Headers.Add(HeaderAuthenticationHandler.Header, "ada");
        return _client!.SendAsync(message);
    }

    [Fact]
    public async Task Authorize_on_the_request_protects_its_endpoint()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post("/open/GetSecretRequest", signedIn: false)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Post("/open/GetSecretRequest", signedIn: true)).StatusCode);
    }

    [Fact]
    public async Task Authorize_on_a_base_request_is_inherited()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post("/open/ResetRequest", signedIn: false)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Post("/open/ResetRequest", signedIn: true)).StatusCode);
    }

    [Fact]
    public async Task AllowAnonymous_on_the_request_opens_it_in_a_protected_group()
    {
        Assert.Equal(HttpStatusCode.OK, (await Post("/closed/GetPublicRequest", signedIn: false)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post("/closed/NoopRequest", signedIn: false)).StatusCode);
    }

    [Fact]
    public async Task Requests_without_attributes_are_unchanged()
    {
        Assert.Equal(HttpStatusCode.NoContent, (await Post("/open/NoopRequest", signedIn: false)).StatusCode);
    }
}

/// <summary>Signs in whoever sends the <see cref="Header"/> header.</summary>
internal sealed class HeaderAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Header";
    public const string Header = "X-User";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Header, out var user))
            return Task.FromResult(AuthenticateResult.NoResult());
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, user.ToString())], SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
