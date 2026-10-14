using Kododo.Reiho.AspNetCore.API;

namespace Kododo.Reiho.AspNetCore.Mcp;

/// <summary>Which requests are offered as MCP tools, and how they are named.</summary>
public sealed class ReihoMcpOptions
{
    private readonly Dictionary<Type, McpToolAttribute> _included = [];

    /// <summary>
    /// Put before every tool name, so tools of several libraries on one MCP server do not clash, e.g. <c>orders_</c>.
    /// </summary>
    public string Prefix { get; set; } = "";

    /// <summary>
    /// Offers <typeparamref name="TRequest"/> as a tool although it has no <see cref="McpToolAttribute"/>, e.g. a request
    /// of a library that cannot reference this package. <paramref name="tool"/> takes the place of the attribute.
    /// </summary>
    public ReihoMcpOptions Include<TRequest>(McpToolAttribute? tool = null) => Include(typeof(TRequest), tool);

    /// <inheritdoc cref="Include{TRequest}"/>
    public ReihoMcpOptions Include(Type requestType, McpToolAttribute? tool = null)
    {
        _included[requestType] = tool ?? new McpToolAttribute();
        return this;
    }

    /// <summary>The settings of the tool for <paramref name="requestType"/>, or <c>null</c> when it is not offered.</summary>
    internal McpToolAttribute? ToolFor(Type requestType) =>
        _included.TryGetValue(requestType, out var tool)
            ? tool
            : requestType.GetCustomAttributes(typeof(McpToolAttribute), inherit: false).OfType<McpToolAttribute>().FirstOrDefault();
}
