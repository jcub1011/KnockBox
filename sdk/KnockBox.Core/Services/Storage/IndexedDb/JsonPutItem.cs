namespace KnockBox.Core.Services.Storage.IndexedDb
{
    /// <summary>
    /// One record in a <see cref="IIndexedDatabase.JsonPutBatchAsync"/> call.
    /// Carries its target store name so a single batch can span multiple
    /// stores under one IDB readwrite transaction; carries the typed value
    /// the caller already has in hand so the database can serialize via its
    /// configured <see cref="System.Text.Json.JsonSerializerOptions"/>
    /// (matching <see cref="IIndexedDatabase.JsonPutSingleAsync{T}"/>'s
    /// pattern); carries a nullable <see cref="Key"/> so stores with
    /// out-of-line auto-generated keys can omit the key.
    /// </summary>
    public readonly record struct JsonPutItem(
        string StoreName,
        object Value,
        IndexedDbKey? Key = null);
}
