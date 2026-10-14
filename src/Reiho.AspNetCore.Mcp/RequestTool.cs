using System.ComponentModel;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Kododo.Reiho.AspNetCore.API;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Kododo.Reiho.AspNetCore.Mcp;

/// <summary>
/// A request offered as an MCP tool. The tool's arguments are the request's properties, so its input schema is the
/// request's JSON schema; a call deserializes them into the request, the same way as the request's HTTP endpoint does,
/// and runs its handler. The result is returned as JSON text.
/// </summary>
internal abstract class RequestTool : McpServerTool
{
    protected RequestTool(Type requestType, McpToolAttribute tool, string prefix)
    {
        RequestType = requestType;
        var description = requestType.GetCustomAttribute<DescriptionAttribute>()?.Description;
        ProtocolTool = new Tool
        {
            Name = prefix + (tool.Name ?? SnakeCase(requestType.Name)),
            Title = tool.Title,
            Description = description,
            InputSchema = InputSchema(requestType),
            Annotations = new ToolAnnotations
            {
                Title = tool.Title,
                ReadOnlyHint = tool.ReadOnly,
                DestructiveHint = tool.ReadOnly ? null : tool.Destructive,
                IdempotentHint = tool.Idempotent,
                OpenWorldHint = tool.OpenWorld,
            },
        };
        // The request's attributes, e.g. [Authorize], for the MCP server's authorization filters.
        Metadata = requestType.GetCustomAttributes(inherit: true);
    }

    public Type RequestType { get; }

    public override Tool ProtocolTool { get; }

    public override IReadOnlyList<object> Metadata { get; }

    public override async ValueTask<CallToolResult> InvokeAsync(
        RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
    {
        object? parsed;
        try
        {
            var arguments = JsonSerializer.SerializeToElement(
                request.Params?.Arguments ?? new Dictionary<string, JsonElement>(), ReihoJson.Options);
            parsed = arguments.Deserialize(RequestType, ReihoJson.Options);
        }
        catch (JsonException e)
        {
            return Error($"The arguments do not fit the input schema: {e.Message}");
        }
        if (parsed is null)
            return Error("The arguments are missing.");

        // The same checks and errors as over HTTP, as text the agent can act on.
        if (RequestValidator.Validate(parsed) is { } errors)
            return Error(Invalid(errors));

        var services = request.Services
            ?? throw new InvalidOperationException("The MCP request has no service provider to resolve the handler from.");
        try
        {
            return await HandleAsync(parsed, services, cancellationToken);
        }
        catch (RequestException e)
        {
            return Error(e.Errors is { } fieldErrors
                ? Invalid(RequestValidator.ByJsonName(fieldErrors))
                : $"{e.StatusCode} {ReasonPhrases.GetReasonPhrase(e.StatusCode)}: {e.Message}");
        }
    }

    private static string Invalid(IReadOnlyDictionary<string, string[]> errors) =>
        "The arguments are not valid:" + string.Concat(errors.SelectMany(field =>
            field.Value.Select(message => field.Key.Length == 0 ? $"\n- {message}" : $"\n- {field.Key}: {message}")));

    protected abstract Task<CallToolResult> HandleAsync(object request, IServiceProvider services, CancellationToken cancellationToken);

    protected static CallToolResult Text(string text) => new() { Content = [new TextContentBlock { Text = text }] };

    // A file as an embedded resource: text the model can read for a text type, base64 otherwise.
    protected static async Task<CallToolResult> FileAsync(FileResponse file, CancellationToken cancellationToken)
    {
        byte[] bytes;
        await using (var content = file.Content)
        {
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            bytes = buffer.ToArray();
        }

        var uri = "file:///" + Uri.EscapeDataString(file.FileName ?? "file");
        var mediaType = file.ContentType.Split(';')[0].Trim().ToLowerInvariant();
        var isText = mediaType.StartsWith("text/") || mediaType is "application/json" or "application/xml" or "application/javascript"
                     || mediaType.EndsWith("+json") || mediaType.EndsWith("+xml");
        ResourceContents resource = isText
            ? new TextResourceContents { Uri = uri, MimeType = file.ContentType, Text = Encoding.UTF8.GetString(bytes) }
            : BlobResourceContents.FromBytes(bytes, uri, file.ContentType);
        return new CallToolResult { Content = [new EmbeddedResourceBlock { Resource = resource }] };
    }

    // An error the agent can read and act on, e.g. by correcting the arguments.
    private static CallToolResult Error(string message) =>
        new() { Content = [new TextContentBlock { Text = message }], IsError = true };

    // The request's description is the tool's; repeated at the top of the schema it would only cost tokens.
    private static JsonElement InputSchema(Type requestType)
    {
        var schema = JsonSerializer.SerializeToNode(AIJsonUtilities.CreateJsonSchema(requestType, serializerOptions: ReihoJson.Options))!.AsObject();
        schema.Remove("description");
        return JsonSerializer.SerializeToElement(schema);
    }

    internal static string SnakeCase(string name)
    {
        var result = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c) && i > 0 && (char.IsLower(name[i - 1]) || i + 1 < name.Length && char.IsLower(name[i + 1])))
                result.Append('_');
            result.Append(char.ToLowerInvariant(c));
        }
        return result.ToString();
    }
}

internal sealed class RequestTool<TRequest>(McpToolAttribute tool, string prefix)
    : RequestTool(typeof(TRequest), tool, prefix)
    where TRequest : IRequest
{
    protected override async Task<CallToolResult> HandleAsync(object request, IServiceProvider services, CancellationToken cancellationToken)
    {
        await services.GetRequiredService<IRequestHandler<TRequest>>().HandleAsync((TRequest)request, cancellationToken);
        return Text("Done.");
    }
}

internal sealed class RequestTool<TRequest, TResult>(McpToolAttribute tool, string prefix)
    : RequestTool(typeof(TRequest), tool, prefix)
    where TRequest : IRequest<TResult>
{
    protected override async Task<CallToolResult> HandleAsync(object request, IServiceProvider services, CancellationToken cancellationToken)
    {
        var result = await services.GetRequiredService<IRequestHandler<TRequest, TResult>>().HandleAsync((TRequest)request, cancellationToken);
        return result is FileResponse file
            ? await FileAsync(file, cancellationToken)
            : Text(JsonSerializer.Serialize(result, result?.GetType() ?? typeof(object), ReihoJson.Options));
    }
}
