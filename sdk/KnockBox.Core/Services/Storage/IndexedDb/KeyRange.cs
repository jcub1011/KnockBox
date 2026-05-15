namespace KnockBox.Core.Services.Storage.IndexedDb
{
    /// <summary>
    /// A continuous range of keys, mirroring <c>IDBKeyRange</c>. Used to scope
    /// <c>GetAll</c>, <c>Count</c>, cursor, and range-delete operations.
    /// <para>
    /// Either bound may be <see langword="null"/> to leave that side unbounded.
    /// The <c>Open</c> flags control whether the bound itself is included
    /// (closed, <see langword="false"/>) or excluded (open, <see langword="true"/>).
    /// </para>
    /// </summary>
    public readonly record struct KeyRange
    {
        public IndexedDbKey? Lower { get; }
        public IndexedDbKey? Upper { get; }
        public bool LowerOpen { get; }
        public bool UpperOpen { get; }

        private KeyRange(IndexedDbKey? lower, IndexedDbKey? upper, bool lowerOpen, bool upperOpen)
        {
            Lower = lower;
            Upper = upper;
            LowerOpen = lowerOpen;
            UpperOpen = upperOpen;
        }

        /// <summary>Matches only the given key.</summary>
        public static KeyRange Only(IndexedDbKey value) => new(value, value, false, false);

        /// <summary>Matches every key greater than (or equal to) <paramref name="value"/>.</summary>
        public static KeyRange LowerBound(IndexedDbKey value, bool open = false) => new(value, null, open, false);

        /// <summary>Matches every key less than (or equal to) <paramref name="value"/>.</summary>
        public static KeyRange UpperBound(IndexedDbKey value, bool open = false) => new(null, value, false, open);

        /// <summary>Matches every key between <paramref name="lower"/> and <paramref name="upper"/>.</summary>
        public static KeyRange Bound(IndexedDbKey lower, IndexedDbKey upper, bool lowerOpen = false, bool upperOpen = false)
            => new(lower, upper, lowerOpen, upperOpen);
    }
}
