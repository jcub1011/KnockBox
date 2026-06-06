using KnockBox.Core.Primitives.Returns;
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

        public override ValueTask<ValueResult<byte[], IndexedDbError>> ReadAllBytesAsync(CancellationToken ct = default)
            => ValueTask.FromResult(ValueResult<byte[], IndexedDbError>.FromValue(_bytes));

        public override ValueTask<ValueResult<Stream, IndexedDbError>> OpenReadAsync(CancellationToken ct = default)
            => ValueTask.FromResult(ValueResult<Stream, IndexedDbError>.FromValue(new MemoryStream(_bytes, writable: false)));

        public override ValueTask<ValueResult<string, IndexedDbError>> CreateObjectUrlAsync(CancellationToken ct = default)
            => ValueTask.FromResult(ValueResult<string, IndexedDbError>.FromValue($"blob:fake/{Guid.NewGuid():D}"));

        public override ValueTask<ValueResult<IBlobShare, IndexedDbError>> PublishForSharingAsync(
            BlobShareOptions? options = null,
            CancellationToken ct = default)
        {
            var share = new FakeBlobShare(ContentType, Length);
            PublishedShares.Add(share);
            return ValueTask.FromResult(ValueResult<IBlobShare, IndexedDbError>.FromValue((IBlobShare)share));
        }

        public override ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
