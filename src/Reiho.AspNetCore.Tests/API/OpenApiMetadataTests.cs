using System.Reflection;
using Kododo.Reiho.AspNetCore.API;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Xunit;

namespace Kododo.Reiho.AspNetCore.Tests.API;

public sealed class OpenApiMetadataTests : IAsyncLifetime
{
    private IHost? _host;

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
                    services.AddEndpointsApiExplorer();
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapRequests(Assembly.GetExecutingAssembly()));
                });
            })
            .StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
    }

    private ApiDescription Describe(string path) =>
        _host!.Services.GetRequiredService<IApiDescriptionGroupCollectionProvider>().ApiDescriptionGroups.Items
            .SelectMany(g => g.Items)
            .Single(d => d.RelativePath == path);

    [Fact]
    public void Result_request_has_its_body_and_result_types()
    {
        var api = Describe("GetGreetingRequest");

        var body = Assert.Single(api.ParameterDescriptions, p => p.Source == BindingSource.Body);
        Assert.Equal(typeof(GetGreetingRequest), body.Type);
        Assert.True(body.IsRequired);
        Assert.Contains(api.SupportedRequestFormats, f => f.MediaType == "application/json");

        var ok = Assert.Single(api.SupportedResponseTypes, r => r.StatusCode == StatusCodes.Status200OK);
        Assert.Equal(typeof(GreetingResult), ok.Type);
        var invalid = Assert.Single(api.SupportedResponseTypes, r => r.StatusCode == StatusCodes.Status400BadRequest);
        Assert.Equal(typeof(HttpValidationProblemDetails), invalid.Type);
        Assert.Contains(invalid.ApiResponseFormats, f => f.MediaType == "application/problem+json");
    }

    [Fact]
    public void Void_request_returns_204_and_its_body_is_optional_with_a_parameterless_ctor()
    {
        var api = Describe("PingRequest");

        var body = Assert.Single(api.ParameterDescriptions, p => p.Source == BindingSource.Body);
        Assert.False(body.IsRequired);
        Assert.Contains(api.SupportedResponseTypes, r => r.StatusCode == StatusCodes.Status204NoContent);
    }

    [Fact]
    public void Endpoint_is_summarized_by_the_request_description()
    {
        var api = Describe("GetGreetingRequest");

        Assert.Equal("Greets a person by name.", api.ActionDescriptor.EndpointMetadata.OfType<IEndpointSummaryMetadata>().Single().Summary);
    }

    [Fact]
    public void Endpoint_is_named_after_its_route()
    {
        var api = Describe("GetGreetingRequest");

        Assert.Equal("GetGreetingRequest", api.ActionDescriptor.EndpointMetadata.OfType<IEndpointNameMetadata>().Single().EndpointName);
    }

    [Fact]
    public async Task Requests_mapped_in_several_groups_get_distinct_names_from_the_group_prefix()
    {
        using var host = await new HostBuilder()
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
                        endpoints.MapGroup("/api").MapRequests(Assembly.GetExecutingAssembly());
                        endpoints.MapGroup("/log-way/{tenant}").MapRequests(Assembly.GetExecutingAssembly());
                    });
                });
            })
            .StartAsync();

        var names = host.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .Select(e => e.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName)
            .ToList();
        Assert.Contains("Api_PingRequest", names);
        Assert.Contains("LogWay_Tenant_PingRequest", names);

        // Duplicate endpoint names would fail every request.
        var response = await host.GetTestClient().PostAsync("/api/GetGreetingRequest",
            new StringContent("""{"name":"Ada"}""", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }
}
