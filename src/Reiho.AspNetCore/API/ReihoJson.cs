using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kododo.Reiho.AspNetCore.API;

/// <summary>How requests and results are written as JSON, the same for every way a request arrives.</summary>
internal static class ReihoJson
{
    // One shared instance: System.Text.Json caches type metadata per options instance.
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary><see cref="Options"/> without indentation, for results read by a model rather than a person.</summary>
    public static JsonSerializerOptions Compact { get; } = new(Options) { WriteIndented = false };
}
