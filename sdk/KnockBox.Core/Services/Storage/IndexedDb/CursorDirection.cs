namespace KnockBox.Core.Services.Storage.IndexedDb
{
    /// <summary>
    /// Direction (and uniqueness filter) of an IndexedDB cursor.
    /// </summary>
    public enum CursorDirection
    {
        /// <summary>Forward iteration, includes duplicate keys.</summary>
        Next = 0,

        /// <summary>Forward iteration, skipping duplicate keys.</summary>
        NextUnique = 1,

        /// <summary>Reverse iteration, includes duplicate keys.</summary>
        Prev = 2,

        /// <summary>Reverse iteration, skipping duplicate keys.</summary>
        PrevUnique = 3,
    }
}
