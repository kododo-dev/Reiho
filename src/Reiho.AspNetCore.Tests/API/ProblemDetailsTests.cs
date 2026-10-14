using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Kododo.Reiho.AspNetCore.API;
using Microsoft.AspNetCore.TestHost;
using Xunit;

namespace Kododo.Reiho.AspNetCore.Tests.API;

public sealed class ProblemDetailsTests : IAsyncLifetime
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
                    services.AddProblemDetails(o => o.CustomizeProblemDetails = c => c.ProblemDetails.Extensions["app"] = "test");
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

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private async Task<(HttpStatusCode Status, JsonElement Problem)> Post(string path, string? json)
    {
        var response = await _client!.PostAsync(path, json is null ? null : Json(json));
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        return (response.StatusCode, JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement);
    }

    private static string[] Errors(JsonElement problem, string field) =>
        problem.GetProperty("errors").GetProperty(field).EnumerateArray().Select(e => e.GetString()!).ToArray();

    // ── Body ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Missing_body_is_a_problem()
    {
        var (status, problem) = await Post("/DeleteItemRequest", null);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(400, problem.GetProperty("status").GetInt32());
        Assert.Equal("A JSON body is required for this request.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Malformed_json_is_a_problem()
    {
        var (status, problem) = await Post("/DeleteItemRequest", "{not json");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("The request body is not valid JSON for this request.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Value_of_the_wrong_type_names_its_field()
    {
        var (status, problem) = await Post("/DeleteItemRequest", """{"id":"seven"}""");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(["The value is not valid."], Errors(problem, "id"));
    }

    // ── Validation ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Data_annotations_on_record_parameters_and_properties_are_checked()
    {
        var (status, problem) = await Post("/CreateUserRequest", """{"name":"Alexandra","age":0,"email":"nope"}""");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Single(Errors(problem, "name"));
        Assert.Single(Errors(problem, "age"));
        Assert.Single(Errors(problem, "email"));
    }

    [Fact]
    public async Task Required_field_missing_from_the_body_is_reported()
    {
        var (_, problem) = await Post("/CreateUserRequest", """{"age":30}""");

        Assert.Equal(["The name field is required."], Errors(problem, "name"));
    }

    [Fact]
    public async Task Validatable_object_runs_once_the_fields_are_valid()
    {
        var (status, problem) = await Post("/CreateUserRequest", """{"name":"root","age":30}""");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(["The name is reserved."], Errors(problem, "name"));
    }

    [Fact]
    public async Task Invalid_request_does_not_reach_the_handler_and_a_valid_one_does()
    {
        CreateUserHandler.CallCount = 0;
        await _client!.PostAsync("/CreateUserRequest", Json("""{"name":"","age":30}"""));
        Assert.Equal(0, CreateUserHandler.CallCount);

        var response = await _client!.PostAsync("/CreateUserRequest", Json("""{"name":"Ada","age":36}"""));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("\"Created Ada.\"", await response.Content.ReadAsStringAsync());
        Assert.Equal(1, CreateUserHandler.CallCount);
    }

    // ── Errors thrown by a handler ────────────────────────────────────────────

    [Theory]
    [InlineData(2, HttpStatusCode.Conflict, "User 2 is being merged.")]
    [InlineData(9, HttpStatusCode.NotFound, "User 9 does not exist.")]
    public async Task Request_exception_becomes_a_problem_with_its_status(int id, HttpStatusCode expected, string detail)
    {
        var (status, problem) = await Post("/FindUserRequest", $$"""{"id":{{id}}}""");

        Assert.Equal(expected, status);
        Assert.Equal((int)expected, problem.GetProperty("status").GetInt32());
        Assert.Equal(detail, problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Invalid_field_from_a_handler_is_a_validation_problem()
    {
        var (status, problem) = await Post("/FindUserRequest", """{"id":3}""");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(["Ids below 10 are reserved."], Errors(problem, "id"));
    }

    [Fact]
    public async Task Other_exceptions_are_left_to_the_application()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _client!.PostAsync("/FindUserRequest", Json("""{"id":4}""")));
    }

    [Fact]
    public async Task Problems_go_through_the_apps_problem_details_service()
    {
        var (_, problem) = await Post("/FindUserRequest", """{"id":9}""");

        Assert.Equal("test", problem.GetProperty("app").GetString());
    }
}
