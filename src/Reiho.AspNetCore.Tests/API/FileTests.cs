using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Kododo.Reiho.AspNetCore.API;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.TestHost;
using Xunit;

namespace Kododo.Reiho.AspNetCore.Tests.API;

public sealed class FileTests
{
    private static async Task<IHost> StartAsync(bool antiforgery)
    {
        return await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddRequestHandlers(Assembly.GetExecutingAssembly());
                    services.AddRouting();
                    services.AddEndpointsApiExplorer();
                    if (antiforgery)
                        services.AddAntiforgery();
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    if (antiforgery)
                        app.UseAntiforgery();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapRequests(Assembly.GetExecutingAssembly());
                        if (antiforgery)
                            endpoints.MapGet("/token", (IAntiforgery tokens, HttpContext ctx) => tokens.GetAndStoreTokens(ctx).RequestToken);
                    });
                });
            })
            .StartAsync();
    }

    private static ByteArrayContent File(string text) => new(Encoding.UTF8.GetBytes(text));

    // ── Uploads ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Form_with_fields_and_files_reaches_the_handler()
    {
        using var host = await StartAsync(antiforgery: false);
        var form = new MultipartFormDataContent
        {
            { new StringContent("7"), "userId" },
            { new StringContent("Blue"), "tint" },
            { File("hello"), "avatar", "ada.png" },
            { File("a"), "extras", "a.txt" },
            { File("b"), "extras", "b.txt" },
        };

        var response = await host.GetTestClient().PostAsync("/UploadAvatarRequest", form);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("\"7:Blue:ada.png:5:2\"", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Missing_required_file_is_a_validation_problem()
    {
        using var host = await StartAsync(antiforgery: false);
        var form = new MultipartFormDataContent { { new StringContent("7"), "userId" }, { new StringContent("Red"), "tint" } };

        var response = await host.GetTestClient().PostAsync("/UploadAvatarRequest", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("avatar", out _));
    }

    [Fact]
    public async Task Field_of_the_wrong_type_is_named()
    {
        using var host = await StartAsync(antiforgery: false);
        var form = new MultipartFormDataContent { { new StringContent("seven"), "userId" }, { File("x"), "avatar", "a.png" } };

        var response = await host.GetTestClient().PostAsync("/UploadAvatarRequest", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("userId", out _));
    }

    [Fact]
    public async Task Request_without_files_does_not_take_a_form()
    {
        using var host = await StartAsync(antiforgery: false);
        var form = new MultipartFormDataContent { { new StringContent("Ada"), "name" } };

        var response = await host.GetTestClient().PostAsync("/GetGreetingRequest", form);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task Form_post_needs_antiforgery_by_default()
    {
        using var host = await StartAsync(antiforgery: false);
        var form = new MultipartFormDataContent { { new StringContent("hi"), "note" } };

        // ASP.NET Core refuses to run the endpoint without the antiforgery middleware rather than skip the check.
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => host.GetTestClient().PostAsync("/AttachNoteRequest", form));
    }

    [Fact]
    public async Task Form_post_is_rejected_without_a_token_and_accepted_with_one()
    {
        using var host = await StartAsync(antiforgery: true);
        var client = host.GetTestClient();

        var rejected = await client.PostAsync("/AttachNoteRequest", new MultipartFormDataContent { { new StringContent("hi"), "note" } });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        var tokenResponse = await client.GetAsync("/token");
        var token = (await tokenResponse.Content.ReadAsStringAsync()).Trim('"');
        var cookie = tokenResponse.Headers.GetValues("Set-Cookie").First().Split(';')[0];
        var message = new HttpRequestMessage(HttpMethod.Post, "/AttachNoteRequest")
        {
            Content = new MultipartFormDataContent
            {
                { new StringContent("hi"), "note" },
                { new StringContent(token), "__RequestVerificationToken" },
                { File("x"), "file", "note.txt" },
            },
        };
        message.Headers.Add("Cookie", cookie);

        var accepted = await client.SendAsync(message);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal("\"hi:note.txt\"", await accepted.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Request_with_files_does_not_take_json()
    {
        using var host = await StartAsync(antiforgery: false);

        var response = await host.GetTestClient().PostAsync("/AttachNoteRequest",
            new StringContent("""{"note":"hi"}""", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    // ── Downloads ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task File_response_is_a_download()
    {
        using var host = await StartAsync(antiforgery: false);

        var response = await host.GetTestClient().PostAsync("/ExportGreetingsRequest",
            new StringContent("""{"format":"csv"}""", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("greetings.csv", response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName);
        Assert.Equal("name\nAda", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task File_response_without_a_name_is_inline_bytes()
    {
        using var host = await StartAsync(antiforgery: false);

        var response = await host.GetTestClient().PostAsync("/ExportGreetingsRequest",
            new StringContent("""{"format":"bin"}""", Encoding.UTF8, "application/json"));

        Assert.Equal("application/octet-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Null(response.Content.Headers.ContentDisposition);
        Assert.Equal([1, 2, 3], await response.Content.ReadAsByteArrayAsync());
    }

    // ── OpenAPI ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Endpoints_describe_forms_and_files()
    {
        using var host = await StartAsync(antiforgery: false);
        var descriptions = host.Services.GetRequiredService<IApiDescriptionGroupCollectionProvider>().ApiDescriptionGroups.Items
            .SelectMany(g => g.Items).ToList();

        var upload = descriptions.Single(d => d.RelativePath == "UploadAvatarRequest");
        Assert.Equal(["multipart/form-data"], upload.SupportedRequestFormats.Select(f => f.MediaType));

        var export = descriptions.Single(d => d.RelativePath == "ExportGreetingsRequest");
        var ok = Assert.Single(export.SupportedResponseTypes, r => r.StatusCode == StatusCodes.Status200OK);
        Assert.Equal(typeof(Stream), ok.Type);
        Assert.Contains(ok.ApiResponseFormats, f => f.MediaType == "application/octet-stream");
    }
}
