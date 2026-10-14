using System.Text;

namespace Kododo.Reiho.AspNetCore.API;

/// <summary>
/// A file a handler returns, e.g. an export: <c>IRequest&lt;FileResponse&gt;</c>. Over HTTP it is the response body
/// with <see cref="ContentType"/>, downloaded as <see cref="FileName"/> when one is given; an MCP tool returns it as a
/// resource, as text for a text type. The content is disposed of once written.
/// </summary>
/// <example>
/// <code>
/// return FileResponse.FromText(csv, "text/csv", "orders.csv");
/// return new FileResponse(File.OpenRead(path), "application/pdf", "invoice.pdf");
/// </code>
/// </example>
public sealed class FileResponse(Stream content, string contentType = "application/octet-stream", string? fileName = null)
{
    public Stream Content { get; } = content;

    public string ContentType { get; } = contentType;

    /// <summary>Name the file is saved as; without one, a browser shows the content rather than downloading it.</summary>
    public string? FileName { get; } = fileName;

    public static FileResponse FromBytes(byte[] content, string contentType = "application/octet-stream", string? fileName = null) =>
        new(new MemoryStream(content, writable: false), contentType, fileName);

    /// <summary>The text as UTF-8; <paramref name="contentType"/> gets <c>charset=utf-8</c> unless it names a charset.</summary>
    public static FileResponse FromText(string text, string contentType = "text/plain", string? fileName = null) =>
        FromBytes(Encoding.UTF8.GetBytes(text),
            contentType.Contains("charset=", StringComparison.OrdinalIgnoreCase) ? contentType : contentType + "; charset=utf-8",
            fileName);
}
