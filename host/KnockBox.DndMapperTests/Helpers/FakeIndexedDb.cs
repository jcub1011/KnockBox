using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Storage.IndexedDb;
using Microsoft.AspNetCore.Components;

namespace KnockBox.DndMapperTests.Helpers
{
    /// <summary>
    /// In-memory <see cref="IIndexedDbService"/> + <see cref="IIndexedDatabase"/>
    /// pair used by library-service tests. Data lives on the service so it
    /// survives DB handle dispose/reopen (mirroring the browser-side IndexedDB
    /// origin store). Only the operations the library service actually calls
    /// are implemented; the rest throw <see cref="NotImplementedException"/>.
    /// </summary>
    internal sealed class FakeIndexedDbService : IIndexedDbService
    {
        // store -> key -> value
        public Dictionary<string, Dictionary<string, object>> JsonStores { get; } = new();
        public Dictionary<string, Dictionary<string, IndexedDbBlob>> BlobStores { get; } = new();

        // Drives the recovery test: when true, the next OpenAsync returns a
        // database whose ObjectStoreNames is missing the expected stores; the
        // flag self-clears after one open so the recreate+reopen sees the real
        // store list.
        public bool MissingStoresOnNextOpen { get; set; }
        public int OpenCallCount { get; private set; }
        public int DeleteDatabaseCallCount { get; private set; }

        // Counts how many times BlobGetSingleAsync was invoked. Used by export
        // tests to assert the server-side path never reads image blobs (the
        // JS-side packer fetches them directly from IndexedDB instead).
        public int BlobReadCalls { get; internal set; }

        public ValueTask<ValueResult<IIndexedDatabase, IndexedDbError>> OpenAsync(
            IndexedDbSchema schema,
            CancellationToken ct = default)
        {
            OpenCallCount++;
            // Make sure every declared store exists as a dictionary so reads
            // against an empty store return null cleanly (matches the real SDK).
            foreach (var store in schema.Stores ?? [])
            {
                if (store.Kind == DeclaredStoreKind.Json && !JsonStores.ContainsKey(store.Name))
                    JsonStores[store.Name] = new();
                if (store.Kind == DeclaredStoreKind.Blob && !BlobStores.ContainsKey(store.Name))
                    BlobStores[store.Name] = new();
            }

            IReadOnlyList<string> reportedStores;
            if (MissingStoresOnNextOpen)
            {
                MissingStoresOnNextOpen = false;
                reportedStores = new[] { "__some_stale_store__" };
            }
            else
            {
                reportedStores = (schema.Stores ?? []).Select(s => s.Name).ToList();
            }

            var db = new FakeIndexedDatabase(this, schema.Name, schema.Version, reportedStores);
            return ValueTask.FromResult(ValueResult<IIndexedDatabase, IndexedDbError>.FromValue(db));
        }

        public ValueTask<Result<IndexedDbError>> DeleteDatabaseAsync(string name, CancellationToken ct = default)
        {
            DeleteDatabaseCallCount++;
            JsonStores.Clear();
            BlobStores.Clear();
            return ValueTask.FromResult(Result<IndexedDbError>.Success);
        }

        public ValueTask<ValueResult<IReadOnlyList<DatabaseInfo>, IndexedDbError>> ListDatabasesAsync(CancellationToken ct = default)
            => ValueTask.FromResult(ValueResult<IReadOnlyList<DatabaseInfo>, IndexedDbError>.FromValue((IReadOnlyList<DatabaseInfo>)Array.Empty<DatabaseInfo>()));

        public ValueTask<IndexedDbBlob> CreateBlobAsync(ReadOnlyMemory<byte> bytes, string contentType, CancellationToken ct = default)
            => ValueTask.FromResult<IndexedDbBlob>(new FakeBlob(bytes.ToArray(), contentType));

        public ValueTask<IndexedDbBlob> CreateBlobAsync(Stream stream, long length, string contentType, bool leaveOpen = false, CancellationToken ct = default)
        {
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            if (!leaveOpen) stream.Dispose();
            return ValueTask.FromResult<IndexedDbBlob>(new FakeBlob(ms.ToArray(), contentType));
        }
    }

    internal sealed class FakeIndexedDatabase : IIndexedDatabase
    {
        private readonly FakeIndexedDbService _service;

        public FakeIndexedDatabase(FakeIndexedDbService service, string name, int version, IReadOnlyList<string> stores)
        {
            _service = service;
            Name = name;
            Version = version;
            ObjectStoreNames = stores;
        }

        public string Name { get; }
        public int Version { get; }
        public IReadOnlyList<string> ObjectStoreNames { get; }
        public bool Disposed { get; private set; }

        public event Func<ValueTask>? VersionChangeRequested
        {
            add { } // no fake-side trigger
            remove { }
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask<ValueResult<long, IndexedDbError>> CountSingleAsync(string storeName, KeyRange? range = null, CancellationToken ct = default)
        {
            if (_service.JsonStores.TryGetValue(storeName, out var json))
                return ValueTask.FromResult(ValueResult<long, IndexedDbError>.FromValue(json.Count));
            if (_service.BlobStores.TryGetValue(storeName, out var blobs))
                return ValueTask.FromResult(ValueResult<long, IndexedDbError>.FromValue((long)blobs.Count));
            return ValueTask.FromResult(ValueResult<long, IndexedDbError>.FromValue(0L));
        }

        public ValueTask<ValueResult<T?, IndexedDbError>> JsonGetSingleAsync<T>(string storeName, IndexedDbKey key, CancellationToken ct = default)
        {
            var k = KeyString(key);
            if (_service.JsonStores.TryGetValue(storeName, out var store) && store.TryGetValue(k, out var raw))
                return ValueTask.FromResult(ValueResult<T?, IndexedDbError>.FromValue((T?)raw));
            return ValueTask.FromResult(ValueResult<T?, IndexedDbError>.FromValue(default(T)));
        }

        public ValueTask<ValueResult<IndexedDbKey, IndexedDbError>> JsonPutSingleAsync<T>(string storeName, T value, IndexedDbKey? key = null, CancellationToken ct = default)
        {
            if (key is null)
                return ValueTask.FromResult(ValueResult<IndexedDbKey, IndexedDbError>.FromError(new IndexedDbError(IndexedDbErrorKind.Unknown, "Fake requires explicit keys.")));
            if (!_service.JsonStores.TryGetValue(storeName, out var store))
            {
                store = new();
                _service.JsonStores[storeName] = store;
            }
            store[KeyString(key.Value)] = value!;
            return ValueTask.FromResult(ValueResult<IndexedDbKey, IndexedDbError>.FromValue(key.Value));
        }

        public ValueTask<ValueResult<IReadOnlyList<IndexedDbKey>, IndexedDbError>> JsonPutBatchAsync(
            IReadOnlyList<JsonPutItem> items, CancellationToken ct = default)
        {
            if (items is null || items.Count == 0)
                return ValueTask.FromResult(ValueResult<IReadOnlyList<IndexedDbKey>, IndexedDbError>.FromValue(Array.Empty<IndexedDbKey>()));

            // Validate up-front so partial writes don't happen on bad input.
            for (var i = 0; i < items.Count; i++)
            {
                if (items[i].Key is null)
                    return ValueTask.FromResult(ValueResult<IReadOnlyList<IndexedDbKey>, IndexedDbError>.FromError(
                        new IndexedDbError(IndexedDbErrorKind.Unknown, "Fake requires explicit keys.")));
            }

            var keys = new IndexedDbKey[items.Count];
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (!_service.JsonStores.TryGetValue(item.StoreName, out var store))
                {
                    store = new();
                    _service.JsonStores[item.StoreName] = store;
                }
                store[KeyString(item.Key!.Value)] = item.Value;
                keys[i] = item.Key.Value;
            }
            return ValueTask.FromResult(ValueResult<IReadOnlyList<IndexedDbKey>, IndexedDbError>.FromValue((IReadOnlyList<IndexedDbKey>)keys));
        }

        public ValueTask<ValueResult<IndexedDbBlob?, IndexedDbError>> BlobGetSingleAsync(string storeName, IndexedDbKey key, CancellationToken ct = default)
        {
            _service.BlobReadCalls++;
            var k = KeyString(key);
            if (_service.BlobStores.TryGetValue(storeName, out var store) && store.TryGetValue(k, out var blob))
                return ValueTask.FromResult(ValueResult<IndexedDbBlob?, IndexedDbError>.FromValue(blob));
            return ValueTask.FromResult(ValueResult<IndexedDbBlob?, IndexedDbError>.FromValue(null));
        }

        public ValueTask<ValueResult<IndexedDbKey, IndexedDbError>> BlobPutSingleAsync(string storeName, IndexedDbBlob blob, IndexedDbKey? key = null, CancellationToken ct = default)
        {
            if (key is null)
                return ValueTask.FromResult(ValueResult<IndexedDbKey, IndexedDbError>.FromError(new IndexedDbError(IndexedDbErrorKind.Unknown, "Fake requires explicit keys.")));
            if (!_service.BlobStores.TryGetValue(storeName, out var store))
            {
                store = new();
                _service.BlobStores[storeName] = store;
            }
            store[KeyString(key.Value)] = blob;
            return ValueTask.FromResult(ValueResult<IndexedDbKey, IndexedDbError>.FromValue(key.Value));
        }

        public ValueTask<Result<IndexedDbError>> DeleteSingleAsync(string storeName, IndexedDbKey key, CancellationToken ct = default)
        {
            var k = KeyString(key);
            if (_service.JsonStores.TryGetValue(storeName, out var json)) json.Remove(k);
            if (_service.BlobStores.TryGetValue(storeName, out var blobs)) blobs.Remove(k);
            return ValueTask.FromResult(Result<IndexedDbError>.Success);
        }

        public ValueTask<Result<IndexedDbError>> ClearStoresAsync(IReadOnlyList<string> storeNames, CancellationToken ct = default)
        {
            foreach (var name in storeNames)
            {
                if (_service.JsonStores.TryGetValue(name, out var json)) json.Clear();
                if (_service.BlobStores.TryGetValue(name, out var blobs)) blobs.Clear();
            }
            return ValueTask.FromResult(Result<IndexedDbError>.Success);
        }

        // The upload flow that calls this is exercised by browser-driven E2E
        // tests, not unit tests; no test currently in this project drives a
        // simulated file input. If a future test needs it, override by
        // assigning AdoptInputElementFilesHandler before invocation.
        public Func<ElementReference, string, AdoptInputFilesOptions, CancellationToken,
            ValueTask<ValueResult<IReadOnlyList<AdoptedInputFile>, IndexedDbError>>>?
            AdoptInputElementFilesHandler { get; set; }

        public ValueTask<ValueResult<IReadOnlyList<AdoptedInputFile>, IndexedDbError>>
            AdoptInputElementFilesAsync(
                ElementReference inputElement,
                string storeName,
                AdoptInputFilesOptions options,
                CancellationToken ct = default)
            => AdoptInputElementFilesHandler is not null
                ? AdoptInputElementFilesHandler(inputElement, storeName, options, ct)
                : ValueTask.FromResult(ValueResult<IReadOnlyList<AdoptedInputFile>, IndexedDbError>.FromError(
                    new IndexedDbError(IndexedDbErrorKind.NotSupported,
                        "FakeIndexedDatabase has no AdoptInputElementFilesHandler set.")));

        private static string KeyString(IndexedDbKey key)
        {
            if (key.Kind != IndexedDbKeyKind.String)
                throw new NotSupportedException($"FakeIndexedDatabase only supports string keys, got {key.Kind}.");
            return (string)key.Value!;
        }
    }
}
