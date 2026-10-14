using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kododo.Reiho.AspNetCore.API;

/// <summary>How requests and results are written as JSON, the same for every way a request arrives.</summary>
internal static class ReihoJson
{
    // One shared instance: System.Text.Json caches type metadata per options instance. Not indented: results are read
    // by programs and models, and indentation only costs bytes and tokens.
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };
}
