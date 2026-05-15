namespace KnockBox.Core.Services.Storage.IndexedDb
{
    /// <summary>
    /// A single record exposed by a cursor.
    /// </summary>
    /// <param name="Key">
    /// For object-store cursors this equals <paramref name="PrimaryKey"/>. For
    /// index cursors it is the index key (the value of the indexed property),
    /// while <paramref name="PrimaryKey"/> is the underlying store key.
    /// </param>
    /// <param name="PrimaryKey">The store's primary key for this record.</param>
    /// <param name="Value">The deserialized record value.</param>
    public readonly record struct CursorEntry<TValue>(IndexedDbKey Key, IndexedDbKey PrimaryKey, TValue Value);
}
