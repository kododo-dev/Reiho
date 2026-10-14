using System.Net;
using System.Reflection;
using Kododo.Reiho.AspNetCore.API;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Xunit;

namespace Kododo.Reiho.AspNetCore.Tests.API;

public sealed class RequestEndpointsTests
{
    private static async Task<IHost> StartAsync(Action<IEndpointRouteBuilder> map) =>
        await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddRequestHandlers(Assembly.GetExecutingAssembly());
                    services.AddRouting();
                    services.AddAuthentication(HeaderAuthenticationHandler.SchemeName)
                        .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, HeaderAuthenticationHandler>(
                            HeaderAuthenticationHandler.SchemeName, null);
                    services.AddAuthorization();
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(map);
                });
            })
            .StartAsync();

    private static Endpoint EndpointOf<TRequest>(IHost host) =>
        host.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .Single(e => e is RouteEndpoint r && r.RoutePattern.RawText!.EndsWith(typeof(TRequest).Name));

    [Fact]
    public async Task Conventions_on_the_result_apply_to_every_request()
    {
        using var host = await StartAsync(e => e.MapRequests(Assembly.GetExecutingAssembly()).WithTags("All"));

        Assert.Equal(["All"], EndpointOf<PingRequest>(host).Metadata.GetMetadata<ITagsMetadata>()?.Tags);
        Assert.Equal(["All"], EndpointOf<GetGreetingRequest>(host).Metadata.GetMetadata<ITagsMetadata>()?.Tags);
    }

    [Fact]
    public async Task For_configures_the_endpoint_of_one_request()
    {
        using var host = await StartAsync(e => e.MapRequests(Assembly.GetExecutingAssembly()).For<NoopRequest>().RequireAuthorization());
        var client = host.GetTestClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/NoopRequest", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/GetNullRequest", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"))).StatusCode);
    }

    [Fact]
    public async Task For_a_type_that_was_not_mapped_says_so()
    {
        KeyNotFoundException? error = null;
        using var host = await StartAsync(e => error = Assert.Throws<KeyNotFoundException>(
            () => e.MapRequests(Assembly.GetExecutingAssembly()).For<GreetingResult>()));

        Assert.Contains(nameof(GreetingResult), error!.Message);
    }

    [Fact]
    public async Task Map_of_one_request_returns_its_endpoint_builder()
    {
        using var host = await StartAsync(e => e.Map<DeleteItemRequest>("/delete").WithTags("One"));

        Assert.Equal(["One"], host.Services.GetRequiredService<EndpointDataSource>().Endpoints.Single()
            .Metadata.GetMetadata<ITagsMetadata>()?.Tags);
        var response = await host.GetTestClient().PostAsync("/delete",
            new StringContent("""{"id":1}""", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
