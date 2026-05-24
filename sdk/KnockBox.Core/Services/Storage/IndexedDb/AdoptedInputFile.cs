namespace KnockBox.Core.Services.Storage.IndexedDb;

/// <summary>
/// Configuration for <see cref="IIndexedDatabase.AdoptInputElementFilesAsync"/>.
/// Strictly storage-layer concerns; content-type-specific decoding (image
/// dimensions, audio duration, etc.) is the caller's responsibility and
/// composes against the returned <see cref="IndexedDbBlob"/> handle via
/// <see cref="IndexedDbBlob.CreateObjectUrlAsync"/> or
/// <see cref="IndexedDbBlob.OpenReadAsync"/>.
/// </summary>
/// <param name="AcceptedTypes">
/// Allow-list of MIME types (as reported by the browser via <c>File.type</c>).
/// <see langword="null"/> accepts any type. The check is convenience-only;
/// the browser's MIME label is derived from the file extension and is not a
/// security boundary — verify the actual format on the caller's side if
/// security is a concern.
/// </param>
/// <param name="MaxBytes">Per-file size cap. Files larger than this are rejected with an error entry rather than failing the batch.</param>
public sealed record AdoptInputFilesOptions(
    IReadOnlyList<string>? AcceptedTypes = null,
    long MaxBytes = long.MaxValue);

/// <summary>
/// One file's outcome from <see cref="IIndexedDatabase.AdoptInputElementFilesAsync"/>.
/// Success entries carry <see cref="Blob"/> and <see cref="Key"/>; failures
/// carry <see cref="Error"/>. Caller is responsible for disposing the
/// <see cref="IndexedDbBlob"/> handles eventually.
/// </summary>
public sealed record AdoptedInputFile(
    string Filename,
    string ContentType,
    long Length,
    Guid? Key,
    IndexedDbBlob? Blob,
    string? Error);
