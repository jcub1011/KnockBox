namespace KnockBox.Core.Services.Storage.IndexedDb
{
    /// <summary>
    /// A value usable as an IndexedDB key. Mirrors the set of types that the
    /// IDB structured-clone algorithm accepts as keys: strings, numbers, dates,
    /// binary buffers, and arrays composed of those.
    /// </summary>
    /// <remarks>
    /// Use the static factories or one of the implicit conversions; the
    /// default-constructed value is <see cref="IndexedDbKeyKind.None"/> and is
    /// not valid for any operation.
    /// </remarks>
    public readonly record struct IndexedDbKey
    {
        public IndexedDbKeyKind Kind { get; }

        private readonly string? _string;
        private readonly double _number;
        private readonly DateTimeOffset _date;
        private readonly ReadOnlyMemory<byte> _binary;
        private readonly IReadOnlyList<IndexedDbKey>? _array;

        private IndexedDbKey(IndexedDbKeyKind kind, string? s, double n, DateTimeOffset d, ReadOnlyMemory<byte> b, IReadOnlyList<IndexedDbKey>? a)
        {
            Kind = kind;
            _string = s;
            _number = n;
            _date = d;
            _binary = b;
            _array = a;
        }

        public static IndexedDbKey String(string value)
            => new(IndexedDbKeyKind.String, value, default, default, default, null);

        public static IndexedDbKey Number(double value)
            => new(IndexedDbKeyKind.Number, null, value, default, default, null);

        public static IndexedDbKey Date(DateTimeOffset value)
            => new(IndexedDbKeyKind.Date, null, default, value, default, null);

        public static IndexedDbKey Binary(ReadOnlyMemory<byte> value)
            => new(IndexedDbKeyKind.Binary, null, default, default, value, null);

        public static IndexedDbKey Array(params IndexedDbKey[] parts)
            => new(IndexedDbKeyKind.Array, null, default, default, default, parts);

        public static implicit operator IndexedDbKey(string value) => String(value);
        public static implicit operator IndexedDbKey(int value) => Number(value);
        public static implicit operator IndexedDbKey(long value) => Number(value);
        public static implicit operator IndexedDbKey(double value) => Number(value);
        public static implicit operator IndexedDbKey(DateTimeOffset value) => Date(value);

        /// <summary>Underlying value for marshalling. Type depends on <see cref="Kind"/>.</summary>
        public object? Value => Kind switch
        {
            IndexedDbKeyKind.String => _string,
            IndexedDbKeyKind.Number => _number,
            IndexedDbKeyKind.Date => _date,
            IndexedDbKeyKind.Binary => _binary,
            IndexedDbKeyKind.Array => _array,
            _ => null,
        };
    }

    public enum IndexedDbKeyKind
    {
        None = 0,
        String,
        Number,
        Date,
        Binary,
        Array,
    }
}
