namespace KnockBox.Core.Services.Storage.IndexedDb
{
    /// <summary>
    /// A capability handle for an <see cref="IndexedDbBlob"/> that is made
    /// fetchable over HTTP for other clients to render (e.g. as an
    /// <c>&lt;img src&gt;</c>). The originating circuit's IndexedDB still owns
    /// the bytes; the host streams them through the share URL without
    /// persisting them anywhere on the server.
    /// <para>
    /// The URL is a capability — anyone holding it can fetch the bytes until
    /// the share is disposed or expires. Disposing this handle revokes the
    /// URL immediately; disposing the originating blob also revokes any
    /// outstanding shares produced from it.
    /// </para>
    /// </summary>
    public interface IBlobShare : IAsyncDisposable
    {
        /// <summary>
        /// Absolute or root-relative URL another client can use to fetch the
        /// blob. Resolves to the host's <c>/blob-share/{token}</c> endpoint.
        /// </summary>
        string Url { get; }

        /// <summary>Opaque per-share identifier embedded in <see cref="Url"/>.</summary>
        Guid Token { get; }

        /// <summary>MIME type the fetch endpoint serves the bytes as.</summary>
        string ContentType { get; }

        /// <summary>Total byte length of the underlying blob.</summary>
        long Length { get; }
    }
}
