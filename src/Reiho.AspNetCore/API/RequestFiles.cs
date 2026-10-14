using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;

namespace Kododo.Reiho.AspNetCore.API;

/// <summary>
/// The properties of a request that receive uploaded files: <see cref="IFormFile"/>, or a list of them such as
/// <c>IReadOnlyList&lt;IFormFile&gt;</c>. A request with any takes <c>multipart/form-data</c>: its other properties
/// come from the form's fields, the files from its file parts of the same name.
/// </summary>
internal static class RequestFiles
{
    private static readonly ConcurrentDictionary<Type, FileProperty[]> Cache = new();

    public static FileProperty[] Of(Type requestType) => Cache.GetOrAdd(requestType, Find);

    private static FileProperty[] Find(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetIndexParameters().Length == 0)
            .Select(p => p.PropertyType == typeof(IFormFile) ? new FileProperty(p, IsList: false)
                : p.PropertyType.IsGenericType && p.PropertyType.GetGenericArguments() is [var item] && item == typeof(IFormFile)
                  && p.PropertyType.IsAssignableFrom(typeof(List<IFormFile>)) ? new FileProperty(p, IsList: true)
                : null)
            .OfType<FileProperty>()
            .Select(f => f.Property.SetMethod is not null ? f : throw new InvalidOperationException(
                $"{type.FullName}.{f.Property.Name} receives files, so it needs a setter or an init accessor."))
            .ToArray();

    /// <summary>The form's fields as JSON for the request's other properties; the files are set by <see cref="SetFiles"/>.</summary>
    public static JsonObject Fields(Type requestType, IFormCollection form)
    {
        var files = Of(requestType);
        var properties = requestType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var json = new JsonObject();
        foreach (var (key, values) in form)
        {
            if (files.Any(f => Matches(f.Property, key)))
                continue;
            // Forms carry fields of their own, e.g. the antiforgery token, so a field the request lacks is left out.
            if (properties.FirstOrDefault(p => Matches(p, key)) is not { PropertyType: var type })
                continue;
            var item = type != typeof(string) && type.IsGenericType && type.GetGenericArguments() is [var argument] ? argument : type;
            json[key] = values.Count == 1
                ? Value(values[0] ?? "", type)
                : new JsonArray(values.Select(v => Value(v ?? "", item)).ToArray());
        }
        return json;
    }

    public static void SetFiles(object request, IFormFileCollection files)
    {
        foreach (var property in Of(request.GetType()))
        {
            var matching = files.Where(f => Matches(property.Property, f.Name)).ToList();
            property.Property.SetValue(request, property.IsList ? matching : matching.FirstOrDefault());
        }
    }

    private static bool Matches(PropertyInfo property, string name) =>
        string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)
        || string.Equals(RequestValidator.JsonName(property), name, StringComparison.OrdinalIgnoreCase);

    // A field is text; it is a JSON number, boolean or object when the property is not text-like and it parses as one.
    private static JsonNode? Value(string text, Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        if (underlying == typeof(string) || underlying == typeof(char) || underlying.IsEnum || underlying == typeof(Guid)
            || underlying == typeof(DateTime) || underlying == typeof(DateTimeOffset) || underlying == typeof(DateOnly)
            || underlying == typeof(TimeOnly) || underlying == typeof(TimeSpan) || underlying == typeof(Uri))
            return JsonValue.Create(text);
        try
        {
            return JsonNode.Parse(text);
        }
        catch (System.Text.Json.JsonException)
        {
            return JsonValue.Create(text);
        }
    }
}

internal sealed record FileProperty(PropertyInfo Property, bool IsList);
