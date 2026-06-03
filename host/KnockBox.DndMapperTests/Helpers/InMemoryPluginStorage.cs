using KnockBox.Core.Plugins;
using KnockBox.Core.Primitives.Returns;

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

        public ValueResult<Stream> OpenRead(string relativePath)
        {
            string normalized = Normalize(relativePath);
            if (!_files.TryGetValue(normalized, out var bytes))
                return ValueResult<Stream>.FromError($"File not found: {normalized}");
            return ValueResult<Stream>.FromValue(new MemoryStream(bytes, writable: false));
        }

        public ValueResult<Stream> OpenWrite(string relativePath)
        {
            string normalized = Normalize(relativePath);
            try
            {
                if (OpenWriteOverride is not null)
                    return ValueResult<Stream>.FromValue(OpenWriteOverride(normalized));
                return ValueResult<Stream>.FromValue(new CapturingStream(this, normalized));
            }
            catch (Exception ex)
            {
                // Lets tests inject a write failure via OpenWriteOverride throwing.
                return ValueResult<Stream>.FromError("Open write failed.", ex.ToString());
            }
        }

        public bool Exists(string relativePath) => _files.ContainsKey(Normalize(relativePath));

        public Result Delete(string relativePath)
        {
            string normalized = Normalize(relativePath);
            try
            {
                if (DeleteOverride is not null)
                {
                    DeleteOverride(normalized);
                    return Result.Success;
                }
                _files.Remove(normalized);
                return Result.Success;
            }
            catch (Exception ex)
            {
                // Lets tests inject a delete failure via DeleteOverride throwing.
                return Result.FromError("Delete failed.", ex.ToString());
            }
        }

        public ValueResult<IReadOnlyList<string>> EnumerateFiles(string relativeDir, string searchPattern)
        {
            string dir = Normalize(relativeDir);
            string prefix = dir.Length == 0 ? string.Empty : dir + "/";
            // We accept "*" only — that's all the engine uses. Don't bother emulating
            // glob matching here.
            IReadOnlyList<string> matches = _files.Keys
                .Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
                .ToArray();
            return ValueResult<IReadOnlyList<string>>.FromValue(matches);
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
