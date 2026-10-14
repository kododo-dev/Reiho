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
        // A field the request does not have is a mistake, e.g. a typo, rather than something to drop silently.
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };
}
