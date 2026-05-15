namespace KnockBox.Core.Services.Storage.IndexedDb
{
    /// <summary>
    /// Categorizes an <see cref="IndexedDbError"/>. Maps loosely onto the
    /// <c>DOMException</c> names that IndexedDB raises in the browser.
    /// </summary>
    /// <remarks>
    /// "Record missing" is intentionally <em>not</em> represented here. Get
    /// operations return <see langword="null"/> for an absent key — only
    /// genuine failures (quota, version, transaction lifecycle, etc.) produce
    /// an <see cref="IndexedDbError"/>.
    /// </remarks>
    public enum IndexedDbErrorKind
    {
        /// <summary>Unmapped DOMException or unexpected JS-side failure.</summary>
        Unknown = 0,

        /// <summary>Unique-index / key-already-exists violation.</summary>
        Constraint,

        /// <summary>Structured-clone or JSON serialization failure.</summary>
        Data,

        /// <summary>Origin or per-DB storage quota exceeded.</summary>
        QuotaExceeded,

        /// <summary>Version mismatch (e.g. opening with a lower version).</summary>
        Version,

        /// <summary>An operation was attempted on a transaction that is no longer active.</summary>
        TransactionInactive,

        /// <summary>Mutation attempted on a read-only transaction.</summary>
        ReadOnly,

        /// <summary>Transaction aborted (by us or by the user agent).</summary>
        Aborted,

        /// <summary>An upgrade was blocked by another open connection.</summary>
        Blocked,

        /// <summary>IndexedDB or a requested feature is not supported by this browser.</summary>
        NotSupported,
    }
}
