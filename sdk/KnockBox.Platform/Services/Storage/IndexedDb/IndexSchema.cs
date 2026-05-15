using System.Text.Json.Serialization;
using KnockBox.Core.Services.Storage.IndexedDb;

namespace KnockBox.Platform.Services.Storage.IndexedDb;

/// <summary>
/// Snapshot of an index's immutable metadata, captured at database-open time
/// and used to back the sync <see cref="IIndex{TValue}.KeyPath"/>,
/// <see cref="IIndex{TValue}.Unique"/>, and
/// <see cref="IIndex{TValue}.MultiEntry"/> properties.
/// </summary>
internal sealed record IndexSchema(
    [property: JsonPropertyName("keyPath")] string[] KeyPath,
    [property: JsonPropertyName("unique")]  bool Unique,
    [property: JsonPropertyName("multiEntry")] bool MultiEntry);

internal sealed record StoreSchema(
    [property: JsonPropertyName("indexes")] Dictionary<string, IndexSchema> Indexes);
