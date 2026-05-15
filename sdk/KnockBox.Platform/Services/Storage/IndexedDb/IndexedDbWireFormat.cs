using System.Text.Json;
using System.Text.Json.Serialization;

namespace KnockBox.Platform.Services.Storage.IndexedDb;

internal static class IndexedDbWireFormat
{
    public static readonly JsonSerializerOptions DefaultJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
