using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace KnockBox.Core.Client.Json;

/// <summary>
/// Deserializes a per-player projection payload (the JSON string delivered by
/// <c>IGameClient.ReceiveView</c>) into a strongly-typed view DTO. Two
/// implementations cover the two trimming regimes: first-party contracts can ship
/// a source-generated <see cref="JsonTypeInfo{T}"/> (trim-safe), while
/// runtime-loaded third-party DTOs fall back to reflection
/// (<c>JsonSerializerIsReflectionEnabledByDefault</c> keeps that linked).
/// </summary>
public interface IProjectionDeserializer<out TView>
{
    /// <summary>Deserializes <paramref name="payloadJson"/> to <typeparamref name="TView"/>.</summary>
    TView? Deserialize(string payloadJson);
}

/// <summary>
/// Shared serializer options for projection payloads. Enums are written as strings
/// server-side (the host's <c>GameViewCoordinator</c> registers
/// <see cref="JsonStringEnumConverter"/>), so the client must read them the same way.
/// </summary>
public static class ProjectionJson
{
    /// <summary>Reflection-based options matching the server's wire format.</summary>
    public static JsonSerializerOptions DefaultOptions { get; } = new()
    {
        Converters = { new JsonStringEnumConverter() },
        PropertyNameCaseInsensitive = true,
    };
}

/// <summary>
/// Reflection fallback used when a contracts assembly ships no source-gen context
/// (the default, and the only option for runtime-unknown third-party DTOs).
/// </summary>
public sealed class ReflectionProjectionDeserializer<TView> : IProjectionDeserializer<TView>
{
    private readonly JsonSerializerOptions _options;

    public ReflectionProjectionDeserializer(JsonSerializerOptions? options = null)
        => _options = options ?? ProjectionJson.DefaultOptions;

    public TView? Deserialize(string payloadJson)
        => JsonSerializer.Deserialize<TView>(payloadJson, _options);
}

/// <summary>
/// Source-generated path: a first-party contracts assembly passes its
/// <see cref="JsonTypeInfo{T}"/> so deserialization survives IL trimming without
/// reflection roots.
/// </summary>
public sealed class SourceGenProjectionDeserializer<TView>(JsonTypeInfo<TView> typeInfo)
    : IProjectionDeserializer<TView>
{
    public TView? Deserialize(string payloadJson)
        => JsonSerializer.Deserialize(payloadJson, typeInfo);
}
