namespace KnockBox.Core.Services.Storage.IndexedDb
{
    /// <summary>
    /// Per-share tuning passed to
    /// <see cref="IndexedDbBlob.PublishForSharingAsync"/>. All fields are
    /// optional and have sensible defaults.
    /// </summary>
    public sealed record BlobShareOptions
    {
        /// <summary>
        /// Maximum lifetime of the share from creation, regardless of access.
        /// When set, the share auto-expires after this duration.
        /// </summary>
        public TimeSpan? AbsoluteExpiry { get; init; }

        /// <summary>
        /// Sliding expiry: the share is removed once no fetch has occurred for
        /// this duration. Independent of <see cref="AbsoluteExpiry"/>; either
        /// or both may be specified.
        /// </summary>
        public TimeSpan? SlidingExpiry { get; init; }

        /// <summary>
        /// HTTP <c>Cache-Control</c> header value sent with each fetch.
        /// Defaults to <c>"no-store, private"</c> so intermediaries don't
        /// cache the bytes — set explicitly for safely-public content (e.g.
        /// lobby-scoped avatars).
        /// </summary>
        public string? CacheControl { get; init; }
    }
}
