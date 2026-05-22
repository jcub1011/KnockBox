/**
 * DnD Mapper image decode + downscale.
 *
 * Public surface:
 *   probeMaxTextureSize()
 *     -> number  (cached after first call; falls back to 8192 when WebGL2 is
 *                 unavailable)
 *   decodeAndMaybeDownscale(objectUrl, contentType, maxLongEdgePx,
 *                            idbDatabaseName, idbStoreName, idbKey)
 *     -> { widthPx, heightPx, originalWidthPx, originalHeightPx,
 *          wasDownscaled }
 *     When wasDownscaled === true, the newly encoded blob has already been
 *     written into IndexedDB at (idbDatabaseName, idbStoreName, idbKey),
 *     overwriting the adopted-original bytes. The new blob is WebP @ q=0.92,
 *     perceptually indistinguishable from the source for typical battle-map
 *     art and substantially smaller than re-encoded PNG.
 *
 * Decode and downscale run inside a Web Worker that owns an OffscreenCanvas,
 * so a 14k×10k upload doesn't freeze the host's main thread for two seconds.
 * The Worker is shared module-scope (lazy-init on first call) — one Worker
 * per page is enough for the upload cadence we expect (a handful of files
 * per batch, queued sequentially via promises here).
 *
 * The IDB write happens on the main thread via a second connection to the
 * plugin's database. IndexedDB supports concurrent connections under
 * snapshot-isolation, so the existing .NET-managed connection sees the new
 * bytes on its next transaction without coordination.
 *
 * Replaces the older dimensions-only dndMapperImageDimensions.js.
 */

let _maxTextureSize = null;
let _worker = null;
let _nextRequestId = 0;
const _pending = new Map();

function ensureWorker() {
    if (_worker !== null) return _worker;
    _worker = new Worker(
        new URL('./dndMapperImageDownscaleWorker.js', import.meta.url),
        { type: 'classic' });
    _worker.onmessage = (e) => {
        const data = e.data || {};
        const pending = _pending.get(data.id);
        if (!pending) return;
        _pending.delete(data.id);
        if (data.ok) {
            pending.resolve({
                widthPx: data.widthPx,
                heightPx: data.heightPx,
                originalWidthPx: data.originalWidthPx,
                originalHeightPx: data.originalHeightPx,
                wasDownscaled: data.wasDownscaled,
                blob: data.blob ?? null,
            });
        } else {
            pending.reject(new Error(data.error || 'worker decode failed'));
        }
    };
    _worker.onerror = (e) => {
        // Fail every in-flight request — the worker is now in an unknown state.
        for (const [, p] of _pending) p.reject(new Error(`worker error: ${e.message}`));
        _pending.clear();
        try { _worker?.terminate(); } catch { /* ignore */ }
        _worker = null;
    };
    return _worker;
}

export function probeMaxTextureSize() {
    if (_maxTextureSize !== null) return _maxTextureSize;
    try {
        const canvas = document.createElement('canvas');
        const gl = canvas.getContext('webgl2') ?? canvas.getContext('webgl');
        if (gl) {
            const v = gl.getParameter(gl.MAX_TEXTURE_SIZE);
            // Some integrated GPUs report absurdly small or zero values when
            // the context is software-only. Treat anything under 4096 as
            // "untrustworthy" and fall back to the safe default.
            _maxTextureSize = (typeof v === 'number' && v >= 4096) ? v : 8192;
            const loseExt = gl.getExtension('WEBGL_lose_context');
            try { loseExt?.loseContext(); } catch { /* ignore */ }
        } else {
            _maxTextureSize = 8192;
        }
    } catch {
        _maxTextureSize = 8192;
    }
    return _maxTextureSize;
}

export async function decodeAndMaybeDownscale(
    objectUrl, contentType, maxLongEdgePx,
    idbDatabaseName, idbStoreName, idbKey) {
    const id = ++_nextRequestId;
    const worker = ensureWorker();
    const result = await new Promise((resolve, reject) => {
        _pending.set(id, { resolve, reject });
        worker.postMessage({ id, url: objectUrl, contentType, maxLongEdgePx });
    });

    if (result.wasDownscaled && result.blob) {
        // Persist the downscaled bytes over the adopted-original key. The
        // platform's existing IDB connection is held by the host's circuit
        // on another thread of control; opening a second connection here is
        // fine — IDB serialises transactions per-store and gives readers
        // snapshot isolation. By the time this promise resolves, the row
        // is fully committed and the .NET side can safely BlobGetSingle it.
        await writeBlobToStore(idbDatabaseName, idbStoreName, idbKey, result.blob);
    }

    return {
        widthPx: result.widthPx,
        heightPx: result.heightPx,
        originalWidthPx: result.originalWidthPx,
        originalHeightPx: result.originalHeightPx,
        wasDownscaled: result.wasDownscaled,
    };
}

function openDb(name) {
    return new Promise((resolve, reject) => {
        // No version specified → opens at the current version without
        // triggering an upgrade. If a versionchange is in flight from another
        // connection, IDB serialises this open behind it.
        const req = indexedDB.open(name);
        req.onsuccess = () => resolve(req.result);
        req.onerror = () => reject(req.error ?? new Error('indexedDB.open failed'));
        req.onblocked = () => reject(new Error('indexedDB.open blocked'));
    });
}

function writeBlobToStore(databaseName, storeName, idbKey, blob) {
    return new Promise(async (resolve, reject) => {
        let db = null;
        try {
            db = await openDb(databaseName);
            const tx = db.transaction(storeName, 'readwrite');
            tx.oncomplete = () => { try { db.close(); } catch { /* ignore */ } resolve(); };
            tx.onerror = () => { try { db.close(); } catch { /* ignore */ } reject(tx.error ?? new Error('IDB put failed')); };
            tx.onabort = () => { try { db.close(); } catch { /* ignore */ } reject(tx.error ?? new Error('IDB tx aborted')); };
            tx.objectStore(storeName).put(blob, idbKey);
        } catch (err) {
            try { db?.close(); } catch { /* ignore */ }
            reject(err);
        }
    });
}

// Back-compat shim so callers migrating from dndMapperImageDimensions.js can
// be swapped over one method at a time. The legacy shape is the
// no-downscale dimension probe — skip the IDB write entirely.
export async function decodeImageDimensionsFromUrl(objectUrl) {
    const id = ++_nextRequestId;
    const worker = ensureWorker();
    const r = await new Promise((resolve, reject) => {
        _pending.set(id, { resolve, reject });
        worker.postMessage({ id, url: objectUrl, contentType: '', maxLongEdgePx: Number.POSITIVE_INFINITY });
    });
    return { widthPx: r.widthPx, heightPx: r.heightPx };
}
