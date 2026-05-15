using KnockBox.Core.Plugins;

namespace KnockBox.DndMapperTests.Helpers
{
    /// <summary>
    /// Test double for <see cref="IPluginStorage"/> that backs files with an in-memory
    /// dictionary. Mirrors the real implementation's path normalization rules
    /// (forward slashes only, no leading slash, reject absolute / rooted / "..").
    /// </summary>
    internal sealed class InMemoryPluginStorage : IPluginStorage
    {
        private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);

        // Hooks to let tests inject failures.
        public Func<string, Stream>? OpenWriteOverride { get; set; }
        public Action<string>? DeleteOverride { get; set; }

        public IReadOnlyDictionary<string, byte[]> Files => _files;

        public Stream OpenRead(string relativePath)
        {
            string normalized = Normalize(relativePath);
            if (!_files.TryGetValue(normalized, out var bytes))
                throw new FileNotFoundException($"File not found: {normalized}");
            return new MemoryStream(bytes, writable: false);
        }

        public Stream OpenWrite(string relativePath)
        {
            string normalized = Normalize(relativePath);
            if (OpenWriteOverride is not null)
                return OpenWriteOverride(normalized);
            return new CapturingStream(this, normalized);
        }

        public bool Exists(string relativePath) => _files.ContainsKey(Normalize(relativePath));

        public void Delete(string relativePath)
        {
            string normalized = Normalize(relativePath);
            if (DeleteOverride is not null)
            {
                DeleteOverride(normalized);
                return;
            }
            _files.Remove(normalized);
        }

        public IEnumerable<string> EnumerateFiles(string relativeDir, string searchPattern)
        {
            string dir = Normalize(relativeDir);
            string prefix = dir.Length == 0 ? string.Empty : dir + "/";
            // We accept "*" only — that's all the engine uses. Don't bother emulating
            // glob matching here.
            return _files.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
        }

        public void Seed(string relativePath, byte[] bytes)
        {
            _files[Normalize(relativePath)] = bytes;
        }

        private static string Normalize(string relativePath)
        {
            if (relativePath is null) throw new ArgumentNullException(nameof(relativePath));
            if (Path.IsPathRooted(relativePath)) throw new ArgumentException("Absolute paths are not allowed.", nameof(relativePath));
            string normalized = relativePath.Replace('\\', '/').Trim('/');
            if (normalized.Split('/').Any(seg => seg == ".."))
                throw new ArgumentException("Path traversal is not allowed.", nameof(relativePath));
            return normalized;
        }

        private sealed class CapturingStream : MemoryStream
        {
            private readonly InMemoryPluginStorage _owner;
            private readonly string _path;
            private bool _committed;

            public CapturingStream(InMemoryPluginStorage owner, string path)
            {
                _owner = owner;
                _path = path;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing && !_committed)
                {
                    _committed = true;
                    _owner._files[_path] = ToArray();
                }
                base.Dispose(disposing);
            }
        }
    }
}
