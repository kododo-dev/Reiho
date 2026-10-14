namespace Kododo.Reiho.AspNetCore.API;

/// <summary>
/// Offers the request as a tool to AI agents over the Model Context Protocol (package
/// <c>Kododo.Reiho.AspNetCore.Mcp</c>). Requests without it are not offered. Describe the request and its properties
/// with <see cref="System.ComponentModel.DescriptionAttribute"/>; agents decide from those descriptions when and how to
/// call the tool.
/// </summary>
/// <example>
/// <code>
/// [McpTool(ReadOnly = true), Description("Finds orders of a customer, newest first.")]
/// public sealed record FindOrders([property: Description("Customer id, e.g. c-63")] string CustomerId) : IRequest&lt;Order[]&gt;;
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class McpToolAttribute : Attribute
{
    /// <summary>Name of the tool; by default the request type's name in snake case (<c>FindOrders</c> → <c>find_orders</c>).</summary>
    public string? Name { get; set; }

    /// <summary>Human-readable title of the tool.</summary>
    public string? Title { get; set; }

    /// <summary>The tool only reads; it changes nothing.</summary>
    public bool ReadOnly { get; set; }

    /// <summary>The tool may delete or overwrite data, rather than only add to it. Ignored for a <see cref="ReadOnly"/> tool.</summary>
    public bool Destructive { get; set; } = true;

    /// <summary>Calling the tool again with the same arguments has no further effect.</summary>
    public bool Idempotent { get; set; }

    /// <summary>The tool reaches outside the application, e.g. the internet; <c>false</c> for its own data.</summary>
    public bool OpenWorld { get; set; }
}
