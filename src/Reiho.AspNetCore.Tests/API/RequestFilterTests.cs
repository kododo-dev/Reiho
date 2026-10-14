using System.Net;
using System.Reflection;
using System.Text;
using Kododo.Reiho.AspNetCore.API;
using Microsoft.AspNetCore.TestHost;
using Xunit;

namespace Kododo.Reiho.AspNetCore.Tests.API;

public sealed class FilterLog
{
    public List<string> Entries { get; } = [];
}

public sealed class OuterFilter(FilterLog log) : IRequestFilter
{
    public async ValueTask<object?> InvokeAsync(RequestFilterContext context, RequestFilterDelegate next)
    {
        log.Entries.Add($"outer:{context.Request.GetType().Name}:{context.Transport}:{context.HttpContext is not null}");
        var result = await next(context);
        log.Entries.Add("outer:end");
        return result;
    }
}

public sealed class InnerFilter(FilterLog log) : IRequestFilter
{
    public ValueTask<object?> InvokeAsync(RequestFilterContext context, RequestFilterDelegate next)
    {
        log.Entries.Add("inner");
        return context.Request switch
        {
            FindUserRequest { Id: 13 } => throw RequestException.Forbidden("User 13 is off limits."),
            GetGreetingRequest { Name: "cached" } => ValueTask.FromResult<object?>(new GreetingResult("From the filter.")),
            _ => next(context),
        };
    }
}

public sealed class RequestFilterTests : IAsyncLifetime
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
                    services.AddSingleton<FilterLog>();
                    services.AddRequestFilter<OuterFilter>();
                    services.AddRequestFilter<InnerFilter>();
                    services.AddRequestFilter<OuterFilter>();
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

    private List<string> Log => _host!.Services.GetRequiredService<FilterLog>().Entries;

    private Task<HttpResponseMessage> Post(string path, string json) =>
        _client!.PostAsync(path, new StringContent(json, Encoding.UTF8, "application/json"));

    [Fact]
    public async Task Filters_run_around_the_handler_in_registration_order_once_each()
    {
        var response = await Post("/GetGreetingRequest", """{"name":"Ada"}""");

        Assert.Equal("""{"message":"Hello, Ada!"}""", await response.Content.ReadAsStringAsync());
        Assert.Equal(["outer:GetGreetingRequest:Http:True", "inner", "outer:end"], Log);
    }

    [Fact]
    public async Task Filter_runs_for_a_request_without_result()
    {
        var response = await _client!.PostAsync("/NoopRequest", content: null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Contains("outer:NoopRequest:Http:True", Log);
    }

    [Fact]
    public async Task Filter_can_answer_without_the_handler()
    {
        var response = await Post("/GetGreetingRequest", """{"name":"cached"}""");

        Assert.Equal("""{"message":"From the filter."}""", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Request_exception_from_a_filter_is_a_problem()
    {
        var response = await Post("/FindUserRequest", """{"id":13}""");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("User 13 is off limits.", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Invalid_request_does_not_reach_the_filters()
    {
        await Post("/CreateUserRequest", """{"age":0}""");

        Assert.Empty(Log);
    }
}
