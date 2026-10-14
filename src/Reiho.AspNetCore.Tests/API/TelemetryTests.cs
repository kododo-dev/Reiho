using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using Kododo.Reiho.AspNetCore.API;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Kododo.Reiho.AspNetCore.Tests.API;

public sealed class TelemetryTests : IAsyncLifetime
{
    private readonly LogCollector _logs = new();
    private IHost? _host;
    private HttpClient? _client;

    public async Task InitializeAsync()
    {
        _host = await new HostBuilder()
            .ConfigureLogging(logging => logging.AddProvider(_logs).SetMinimumLevel(LogLevel.Debug))
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

    private Task<HttpResponseMessage> Post(string path, string json) =>
        _client!.PostAsync(path, new StringContent(json, Encoding.UTF8, "application/json"));

    [Fact]
    public async Task Each_request_gets_an_activity_tagged_with_its_name_and_transport()
    {
        var activities = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ReihoTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activities.Add,
        };
        ActivitySource.AddActivityListener(listener);

        await Post("/FindUserRequest", """{"id":1}""");
        await Post("/FindUserRequest", """{"id":404}""");

        var found = activities.Where(a => a.DisplayName == nameof(FindUserRequest)).ToList();
        Assert.Contains(found, a => (string?)a.GetTagItem("reiho.transport") == "http" && a.GetTagItem("reiho.status_code") is null);
        Assert.Contains(found, a => a.GetTagItem("reiho.status_code") is 404);
    }

    [Fact]
    public async Task Requests_turned_away_are_logged_at_debug()
    {
        await Post("/CreateUserRequest", """{"age":0}""");
        await Post("/FindUserRequest", """{"id":404}""");
        await Post("/DeleteItemRequest", "{not json");

        var logs = _logs.Entries.Where(e => e.Category == ReihoTelemetry.LogCategory).ToList();
        Assert.All(logs, e => Assert.Equal(LogLevel.Debug, e.Level));
        Assert.Contains(logs, e => e.Message.StartsWith("CreateUserRequest over Http was turned away: not valid: ") && e.Message.Contains("name"));
        Assert.Contains(logs, e => e.Message == "FindUserRequest over Http ended with 404: User 404 does not exist.");
        Assert.Contains(logs, e => e.Message == "DeleteItemRequest over Http was turned away: The request body is not valid JSON for this request.");
    }

    private sealed class LogCollector : ILoggerProvider
    {
        public ConcurrentQueue<(string Category, LogLevel Level, string Message)> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Logger(categoryName, Entries);

        public void Dispose() { }

        private sealed class Logger(string category, ConcurrentQueue<(string, LogLevel, string)> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                entries.Enqueue((category, logLevel, formatter(state, exception)));
        }
    }
}
