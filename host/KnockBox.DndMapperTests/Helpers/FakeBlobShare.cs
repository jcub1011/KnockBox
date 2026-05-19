using KnockBox.Core.Services.Storage.IndexedDb;

namespace KnockBox.DndMapperTests.Helpers
{
    internal sealed class FakeBlobShare : IBlobShare
    {
        public FakeBlobShare(string contentType, long length)
        {
            Token = Guid.NewGuid();
            Url = $"/blob-share/{Token:D}";
            ContentType = contentType;
            Length = length;
        }

        public string Url { get; }
        public Guid Token { get; }
        public string ContentType { get; }
        public long Length { get; }
        public bool Disposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
