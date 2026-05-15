using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using KnockBox.Core.Services.Storage.IndexedDb;

namespace KnockBox.Platform.Services.Storage.IndexedDb;

internal static class IndexedDbWireFormat
{
    /// <summary>
    /// Serializer options used by <see cref="IndexedDbInterop"/> for envelope
    /// unwrapping and by the upgrade-op pipeline. Case-insensitive on the
    /// inbound side so JS-side camelCase deserializes into C# PascalCase
    /// records; camelCase on the outbound side so SchemaOp etc. land as
    /// JS-shaped property names.
    /// </summary>
    public static readonly JsonSerializerOptions DefaultJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // ------------------------------------------------------------------
    // IndexedDbKey envelope: { kind, value }
    // ------------------------------------------------------------------

    public static object? ToKeyEnvelope(IndexedDbKey? key)
        => key.HasValue ? ToKeyEnvelope(key.Value) : null;

    public static object ToKeyEnvelope(IndexedDbKey key) => key.Kind switch
    {
        IndexedDbKeyKind.String => new { kind = "string", value = (string)key.Value! },
        IndexedDbKeyKind.Number => new { kind = "number", value = Convert.ToDouble(key.Value, CultureInfo.InvariantCulture) },
        IndexedDbKeyKind.Date   => new { kind = "date",   value = ((DateTimeOffset)key.Value!).ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) },
        IndexedDbKeyKind.Binary => new { kind = "binary", value = Convert.ToBase64String(((ReadOnlyMemory<byte>)key.Value!).Span) },
        IndexedDbKeyKind.Array  => new { kind = "array",  value = ((IReadOnlyList<IndexedDbKey>)key.Value!).Select(k => ToKeyEnvelope(k)).ToArray() },
        IndexedDbKeyKind.None   => throw new ArgumentException(
            "Default-constructed IndexedDbKey has kind 'None' and cannot cross the wire.", nameof(key)),
        _ => throw new ArgumentOutOfRangeException(nameof(key), key.Kind, "Unknown IndexedDbKey kind."),
    };

    public static IndexedDbKey FromKeyEnvelope(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Key envelope must be a JSON object.");

        var kind = element.GetProperty("kind").GetString();
        var value = element.GetProperty("value");

        return kind switch
        {
            "string" => IndexedDbKey.String(value.GetString() ?? string.Empty),
            "number" => IndexedDbKey.Number(value.GetDouble()),
            "date"   => IndexedDbKey.Date(DateTimeOffset.Parse(value.GetString()!, CultureInfo.InvariantCulture)),
            "binary" => IndexedDbKey.Binary(Convert.FromBase64String(value.GetString()!)),
            "array"  => IndexedDbKey.Array(value.EnumerateArray().Select(FromKeyEnvelope).ToArray()),
            _ => throw new InvalidOperationException($"Unknown key envelope kind: {kind}"),
        };
    }

    // ------------------------------------------------------------------
    // KeyRange envelope: { lower?, upper?, lowerOpen, upperOpen }
    // ------------------------------------------------------------------

    public static object? ToRangeEnvelope(KeyRange? range)
    {
        if (!range.HasValue) return null;
        var r = range.Value;
        return new
        {
            lower = r.Lower.HasValue ? ToKeyEnvelope(r.Lower.Value) : null,
            upper = r.Upper.HasValue ? ToKeyEnvelope(r.Upper.Value) : null,
            lowerOpen = r.LowerOpen,
            upperOpen = r.UpperOpen,
        };
    }
}
