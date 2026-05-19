using KnockBox.Core.Services.Storage.IndexedDb;

namespace KnockBox.DndMapperTests.Helpers
{
    internal sealed class FakeBlob : IndexedDbBlob
    {
        private readonly byte[] _bytes;

        public FakeBlob(byte[] bytes, string contentType)
        {
            _bytes = bytes;
            ContentType = contentType;
        }

        public override string ContentType { get; }
        public override long Length => _bytes.Length;
        public bool Disposed { get; private set; }
        public List<FakeBlobShare> PublishedShares { get; } = [];

        public override ValueTask<byte[]> ReadAllBytesAsync(CancellationToken ct = default)
            => ValueTask.FromResult(_bytes);

        public override ValueTask<Stream> OpenReadAsync(CancellationToken ct = default)
            => ValueTask.FromResult<Stream>(new MemoryStream(_bytes, writable: false));

        public override ValueTask<string> CreateObjectUrlAsync(CancellationToken ct = default)
            => ValueTask.FromResult($"blob:fake/{Guid.NewGuid():D}");

        public override ValueTask<IBlobShare> PublishForSharingAsync(
            BlobShareOptions? options = null,
            CancellationToken ct = default)
        {
            var share = new FakeBlobShare(ContentType, Length);
            PublishedShares.Add(share);
            return ValueTask.FromResult<IBlobShare>(share);
        }

        public override ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
