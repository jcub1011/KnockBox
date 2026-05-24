/**
 * Triggers a browser download by clicking a hidden <a download> against the
 * given object URL. The C# caller owns the underlying IndexedDbBlob and
 * revokes the URL by disposing the blob; this helper does not revoke.
 *
 * Used by HostSavesPanel to deliver `.vtf` exports built on the server side
 * (wrapped as IndexedDbBlobs) to the user's filesystem.
 */
export function downloadObjectUrl(objectUrl, filename) {
    const a = document.createElement("a");
    a.href = objectUrl;
    a.download = filename || "download";
    a.rel = "noopener";
    document.body.appendChild(a);
    a.click();
    a.remove();
}
