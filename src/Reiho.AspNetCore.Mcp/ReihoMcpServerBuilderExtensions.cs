using System.Reflection;
using Kododo.Reiho.AspNetCore.API;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace Kododo.Reiho.AspNetCore.Mcp;

public static class ReihoMcpServerBuilderExtensions
{
    extension(IMcpServerBuilder builder)
    {
        /// <summary>
        /// Offers the requests of <paramref name="assembly"/> marked with <see cref="McpToolAttribute"/>, and those
        /// included in <paramref name="configure"/>, as MCP tools. Each runs its handler, so register the handlers with
        /// <c>AddRequestHandlers</c> as for the HTTP endpoints.
        /// </summary>
        /// <remarks>
        /// A tool call does not go through the requests' HTTP endpoints, so their endpoint filters and authorization
        /// do not apply. Protect the MCP endpoint itself (<c>app.MapMcp().RequireAuthorization()</c>); an
        /// <c>[Authorize]</c> attribute on a request is honoured per tool once <c>AddAuthorizationFilters()</c> is called.
        /// </remarks>
        /// <example>
        /// <code>
        /// builder.Services.AddRequestHandlers(typeof(Program).Assembly);
        /// builder.Services.AddMcpServer().WithHttpTransport().WithRequests(typeof(Program).Assembly);
        /// app.MapMcp("/mcp").RequireAuthorization();
        /// </code>
        /// </example>
        public IMcpServerBuilder WithRequests(Assembly assembly, Action<ReihoMcpOptions>? configure = null)
        {
            var options = new ReihoMcpOptions();
            configure?.Invoke(options);

            var tools = new List<McpServerTool>();
            foreach (var (request, result) in RequestType.Find(assembly))
            {
                if (options.ToolFor(request) is not { } tool)
                    continue;
                if (RequestFiles.Of(request).Length > 0)
                    throw new InvalidOperationException(
                        $"{request.FullName} receives files, which an MCP tool cannot be given. Offer it over HTTP only.");
                var toolType = result is null
                    ? typeof(RequestTool<>).MakeGenericType(request)
                    : typeof(RequestTool<,>).MakeGenericType(request, result);
                tools.Add((McpServerTool)Activator.CreateInstance(toolType, tool, options.Prefix)!);
            }

            var duplicate = tools.GroupBy(t => t.ProtocolTool.Name).FirstOrDefault(g => g.Count() > 1);
            if (duplicate is not null)
                throw new InvalidOperationException(
                    $"Several requests are offered as the MCP tool '{duplicate.Key}': " +
                    string.Join(", ", duplicate.Cast<RequestTool>().Select(t => t.RequestType.FullName)) + ". Name them with [McpTool(Name = ...)].");

            return builder.WithTools(tools);
        }
    }
}
