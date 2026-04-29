using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

namespace Kododo.Reiho.AspNetCore.SPA;

public static class ReihoSpaEndpointRouteBuilderExtensions
{
    private const string IndexFileName = "index.html";
    private const string DefaultBasePathPlaceholder = "__BASE_PATH__";
    private const string DefaultRootPath = "SPA/dist";
    private const string ImmutableCacheControl = "public, max-age=31536000, immutable";
    
    private static readonly ConcurrentDictionary<(Assembly, string), ConcurrentDictionary<string, CachedFile>>
        PathCache = new();

    private static readonly FileExtensionContentTypeProvider ContentTypeProvider = new();

    extension(IEndpointRouteBuilder endpoints)
    {
        public IEndpointRouteBuilder MapEmbeddedSpa(
            string rootPath = DefaultRootPath,
            string basePathPlaceholder = DefaultBasePathPlaceholder)
        {
            return endpoints.MapEmbeddedSpa(Assembly.GetCallingAssembly(), rootPath, basePathPlaceholder);
        }

        public IEndpointRouteBuilder MapEmbeddedSpa(
            Assembly assembly,
            string rootPath = DefaultRootPath,
            string basePathPlaceholder = DefaultBasePathPlaceholder)
        {
            var provider = new ManifestEmbeddedFileProvider(assembly, rootPath);
            var fileCache = PathCache.GetOrAdd((assembly, rootPath),
                _ => new ConcurrentDictionary<string, CachedFile>());

            endpoints.MapGet("/{**filePath}", async context =>
            {
                var endpoint = context.GetEndpoint() as RouteEndpoint;
                var fullPath = context.Request.Path.Value ?? "";
                var routePattern = endpoint?.RoutePattern.RawText ?? "";
                var groupPrefix = routePattern.Replace("/{**filePath}", "");
                var filePath = fullPath[groupPrefix.Length..];

                bool isIndex;

                if (string.IsNullOrEmpty(filePath) || filePath == "/")
                {
                    filePath = IndexFileName;
                    isIndex = true;
                }
                else
                {
                    isIndex = filePath.Equals(IndexFileName, StringComparison.OrdinalIgnoreCase);
                }

                if (fileCache.TryGetValue(filePath, out var cached))
                {
                    if (!isIndex)
                        context.Response.Headers.CacheControl = ImmutableCacheControl;

                    context.Response.ContentType = cached.ContentType;
                    await context.Response.Body.WriteAsync(cached.Data, context.RequestAborted);
                    return;
                }

                var fileInfo = provider.GetFileInfo(filePath);
                if (!fileInfo.Exists)
                {
                    context.Response.StatusCode = 404;
                    return;
                }

                await using var stream = fileInfo.CreateReadStream();
                string contentType;
                byte[] fileBytes;

                if (isIndex)
                {
                    contentType = "text/html";
                    context.Response.Headers.CacheControl = "no-store";

                    using var reader = new StreamReader(stream);
                    var html = await reader.ReadToEndAsync();
                    html = html.Replace(basePathPlaceholder, CalculateBasePath(context));
                    fileBytes = System.Text.Encoding.UTF8.GetBytes(html);
                }
                else
                {
                    context.Response.Headers.CacheControl = ImmutableCacheControl;

                    using var ms = new MemoryStream();
                    await stream.CopyToAsync(ms);
                    fileBytes = ms.ToArray();

                    if (!ContentTypeProvider.TryGetContentType(fileInfo.Name, out var detected))
                        detected = "application/octet-stream";
                    contentType = detected;
                }

                // index.html is intentionally not cached: base path is computed
                // per-request and the response carries Cache-Control: no-store.
                if (!isIndex)
                    fileCache[filePath] = new CachedFile(fileBytes, contentType);

                context.Response.ContentType = contentType;
                await context.Response.Body.WriteAsync(fileBytes, context.RequestAborted);
            });

            return endpoints;
        }
    }

    private static string CalculateBasePath(HttpContext context)
    {
        var pathBase = context.Request.PathBase.HasValue ? context.Request.PathBase.Value : "";
        var path     = context.Request.Path.HasValue     ? context.Request.Path.Value     : "";

        string basePath;
        if (path.EndsWith("/index.html", StringComparison.OrdinalIgnoreCase))
            basePath = pathBase + path[..^"/index.html".Length];
        else if (path.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            var lastSlash = path.LastIndexOf('/');
            basePath = lastSlash > 0 ? pathBase + path[..(lastSlash + 1)] : pathBase + "/";
        }
        else
        {
            basePath = pathBase + path;
            if (!basePath.EndsWith('/')) basePath += "/";
        }

        if (!basePath.StartsWith('/')) basePath = "/" + basePath;
        if (!basePath.EndsWith('/'))   basePath += "/";
        return basePath;
    }
}