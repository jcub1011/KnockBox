namespace KnockBox.Core.Services.Storage.IndexedDb
{
    /// <summary>
    /// An IndexedDB key path. Either a single dotted property path
    /// (e.g. <c>"id"</c> or <c>"user.email"</c>) or a composite of several paths
    /// forming a tuple-style key.
    /// </summary>
    public readonly record struct KeyPath
    {
        /// <summary>The path segments. Always at least one element.</summary>
        public IReadOnlyList<string> Paths { get; }

        /// <summary>True when this is a composite key path with more than one segment.</summary>
        public bool IsComposite => Paths.Count > 1;

        private KeyPath(IReadOnlyList<string> paths)
        {
            Paths = paths;
        }

        public static KeyPath Single(string path) => new([path]);

        public static KeyPath Composite(params string[] paths) => new(paths);

        public static implicit operator KeyPath(string path) => Single(path);
    }
}
