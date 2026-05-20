/**
 * Decodes natural pixel dimensions of an image at a `blob:` URL using the
 * browser's own image pipeline. Doubles as format validation — a corrupt
 * or non-image payload fails to load and the promise rejects.
 *
 * Used by DndMapperLibraryService after IIndexedDatabase.AdoptInputElementFilesAsync
 * to learn each adopted image's natural size without coupling the shared
 * Platform-level adoption API to image-specific concerns. The `objectUrl`
 * is produced via IndexedDbBlob.CreateObjectUrlAsync, so the bytes never
 * leave the browser.
 */
export function decodeImageDimensionsFromUrl(objectUrl) {
    return new Promise((resolve, reject) => {
        const img = new Image();
        img.onload = () => {
            resolve({ widthPx: img.naturalWidth, heightPx: img.naturalHeight });
        };
        img.onerror = () => {
            reject(new Error("image failed to decode"));
        };
        img.src = objectUrl;
    });
}
