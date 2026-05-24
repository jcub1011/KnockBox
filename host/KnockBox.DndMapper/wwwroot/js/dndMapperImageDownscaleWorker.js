/**
 * Worker side of dndMapperImageDownscale.js. Runs on its own thread so a
 * large fetch + createImageBitmap + canvas.convertToBlob cycle doesn't stall
 * the host's Blazor circuit or rendering thread.
 *
 * Receives: { id, url, contentType, maxLongEdgePx }
 * Replies:  { id, ok, widthPx, heightPx, originalWidthPx, originalHeightPx,
 *             wasDownscaled, blob?, error? }
 *
 * `url` is typically a blob: URL minted on the main thread via
 * IndexedDbBlob.CreateObjectUrlAsync. blob: URLs created by the main thread
 * are inheritable by Workers under the same origin, so fetch(url) resolves.
 */

self.onmessage = async (e) => {
    const { id, url, contentType, maxLongEdgePx } = e.data || {};
    try {
        const response = await fetch(url);
        if (!response.ok) throw new Error(`fetch failed (${response.status})`);
        const sourceBlob = await response.blob();

        // createImageBitmap decodes via the browser's own image pipeline on
        // the worker thread. Honours EXIF orientation for JPEGs by default
        // ('imageOrientation: from-image' is the spec default in workers).
        const bitmap = await createImageBitmap(sourceBlob);
        const originalWidthPx = bitmap.width;
        const originalHeightPx = bitmap.height;

        const longEdge = Math.max(originalWidthPx, originalHeightPx);
        if (!Number.isFinite(maxLongEdgePx) || longEdge <= maxLongEdgePx) {
            bitmap.close?.();
            self.postMessage({
                id,
                ok: true,
                wasDownscaled: false,
                widthPx: originalWidthPx,
                heightPx: originalHeightPx,
                originalWidthPx,
                originalHeightPx,
                blob: null,
            });
            return;
        }

        const scale = maxLongEdgePx / longEdge;
        const targetWidth = Math.max(1, Math.round(originalWidthPx * scale));
        const targetHeight = Math.max(1, Math.round(originalHeightPx * scale));

        const canvas = new OffscreenCanvas(targetWidth, targetHeight);
        const ctx = canvas.getContext('2d', { alpha: true });
        if (!ctx) throw new Error('OffscreenCanvas 2d context unavailable');
        // High-quality downsample. createImageBitmap → drawImage chain uses
        // the browser's resampler (typically Lanczos-ish on desktop Chromium,
        // bilinear on mobile); imageSmoothingQuality nudges Chromium toward
        // the better filter.
        ctx.imageSmoothingEnabled = true;
        ctx.imageSmoothingQuality = 'high';
        ctx.drawImage(bitmap, 0, 0, targetWidth, targetHeight);
        bitmap.close?.();

        // WebP @ q=0.92 is visually indistinguishable from source for map art
        // and ~5–10× smaller than re-encoded PNG. The plugin already accepts
        // image/webp uploads, so the rest of the pipeline doesn't care which
        // encoding came out of here.
        const outBlob = await canvas.convertToBlob({ type: 'image/webp', quality: 0.92 });

        self.postMessage({
            id,
            ok: true,
            wasDownscaled: true,
            widthPx: targetWidth,
            heightPx: targetHeight,
            originalWidthPx,
            originalHeightPx,
            blob: outBlob,
        });
    } catch (err) {
        self.postMessage({
            id,
            ok: false,
            error: (err && err.message) || String(err),
        });
    }
};
