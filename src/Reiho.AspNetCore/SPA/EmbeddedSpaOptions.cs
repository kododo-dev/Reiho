using Microsoft.AspNetCore.Http;

namespace Kododo.Reiho.AspNetCore.SPA;

/// <summary>Options of <c>MapEmbeddedSpa</c>.</summary>
public sealed class EmbeddedSpaOptions
{
    private readonly List<(string Placeholder, Func<HttpContext, ValueTask<string>> Value)> _placeholders = [];

    /// <summary>Folder of the built SPA inside the embedded resources. Default: <c>SPA/dist</c>.</summary>
    public string RootPath { get; set; } = "SPA/dist";

    /// <summary>Text in <c>index.html</c> replaced with the base path of the request. Default: <c>__BASE_PATH__</c>.</summary>
    public string BasePathPlaceholder { get; set; } = "__BASE_PATH__";

    internal IReadOnlyList<(string Placeholder, Func<HttpContext, ValueTask<string>> Value)> Placeholders => _placeholders;

    /// <summary>
    /// Replaces <paramref name="placeholder"/> in <c>index.html</c> with a value computed for each request, e.g. a
    /// script with settings or a CSP nonce. The value is inserted as is: encode it for where it lands in the HTML.
    /// Placeholders are replaced after the base path, in the order they were added.
    /// </summary>
    public EmbeddedSpaOptions Placeholder(string placeholder, Func<HttpContext, string> value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Placeholder(placeholder, context => ValueTask.FromResult(value(context)));
    }

    /// <inheritdoc cref="Placeholder(string, Func{HttpContext, string})"/>
    public EmbeddedSpaOptions Placeholder(string placeholder, Func<HttpContext, ValueTask<string>> value)
    {
        ArgumentException.ThrowIfNullOrEmpty(placeholder);
        ArgumentNullException.ThrowIfNull(value);
        _placeholders.Add((placeholder, value));
        return this;
    }
}
