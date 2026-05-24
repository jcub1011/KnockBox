namespace KnockBox.Core.Services.Storage.IndexedDb
{
    /// <summary>
    /// A binary payload that round-trips between .NET and a JS-side
    /// <c>Blob</c> stored in IndexedDB. Bytes cross the SignalR boundary in
    /// bounded chunks (same pattern as
    /// <see cref="Components.Shared.SvgDrawingEngine"/>) so no single message
    /// approaches the server's <c>MaximumReceiveMessageSize</c> limit.
    /// <para>
    /// A blob retrieved from a store holds a live JS reference and, once
    /// <see cref="CreateObjectUrlAsync"/> has been called, a generated object
    /// URL — callers MUST dispose to release them. Construct new blobs via
    /// <c>IIndexedDbService.CreateBlob*</c>.
    /// </para>
    /// </summary>
    public abstract class IndexedDbBlob : IAsyncDisposable
    {
        /// <summary>
        /// MIME type of the payload (e.g. <c>"image/png"</c>). The full RFC
        /// 7231 media-type grammar is accepted, including parameters such as
        /// <c>"text/plain; charset=utf-8"</c>; the value is not normalized.
        /// </summary>
        public abstract string ContentType { get; }

        /// <summary>Total byte length of the payload.</summary>
        public abstract long Length { get; }

        /// <summary>
        /// Reads the entire blob into a single buffer. Convenient for small
        /// payloads; prefer <see cref="OpenReadAsync"/> for media.
        /// </summary>
        public abstract ValueTask<byte[]> ReadAllBytesAsync(CancellationToken ct = default);

        /// <summary>
        /// Opens a read-only forward stream that pulls chunks from JS on
        /// demand. The returned stream supports <c>ReadAsync</c> only;
        /// synchronous <c>Read</c> throws <see cref="NotSupportedException"/>.
        /// Disposing the stream does not dispose the blob.
        /// </summary>
        public abstract ValueTask<Stream> OpenReadAsync(CancellationToken ct = default);

        /// <summary>
        /// Creates (and caches) a <c>blob:</c> object URL pointing at the
        /// underlying JS blob, suitable for binding to <c>&lt;img src&gt;</c>,
        /// <c>&lt;audio src&gt;</c>, etc. The URL is revoked when this blob is
        /// disposed.
        /// </summary>
        public abstract ValueTask<string> CreateObjectUrlAsync(CancellationToken ct = default);

        /// <summary>
        /// Publishes this blob as an HTTP-fetchable resource that other
        /// clients can render directly (e.g. an <c>&lt;img src&gt;</c> on a
        /// different user's browser). The server does NOT persist the bytes
        /// — when the share URL is fetched, the host streams chunks from
        /// this blob's originating circuit straight into the HTTP response,
        /// holding only one chunk buffer in flight.
        /// <para>
        /// The returned <see cref="IBlobShare"/> must be disposed (or its
        /// owning blob disposed) to revoke the URL. The capability URL
        /// itself is unguessable (a fresh <see cref="Guid"/>); document and
        /// scope its distribution accordingly.
        /// </para>
        /// </summary>
        public abstract ValueTask<IBlobShare> PublishForSharingAsync(
            BlobShareOptions? options = null,
            CancellationToken ct = default);

        public abstract ValueTask DisposeAsync();
    }
}
