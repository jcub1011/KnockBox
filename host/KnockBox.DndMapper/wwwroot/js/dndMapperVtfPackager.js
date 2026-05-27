// Client-side VTF (Virtual Table Format) packager.
//
// The previous export path read every image blob out of IndexedDB through
// the SignalR circuit, packed the ZIP on the server, then marshaled the
// resulting archive back to the browser as another IndexedDb blob. For
// big slots (lots of map images) that's two giant round-trips of binary.
//
// This module keeps image bytes in the browser: the server hands us only
// the small JSON shards (manifest, global state, scenes, entities,
// extension) plus a list of image refs. We open the existing IndexedDB
// database directly, read each image blob, assemble the ZIP locally with
// the browser's native CompressionStream, and download it via the file-
// download helper that already shipped with the host page.
//
// ZIP layout matches VtfPackager.Pack exactly: per-entry [local file
// header | data] followed by a central directory with one record per
// entry and an EOCD record. JSON entries are deflate-compressed; image
// bytes are stored uncompressed (matching the server-side `CompressionLevel
// .NoCompression` choice — PNG/JPG/WebP are already compressed).
//
// Browser support: CompressionStream('deflate-raw') is the gating API.
// Chrome 80+, Firefox 113+, Safari 16.4+. Any environment older than that
// would need a JS deflate polyfill, which we don't ship — the file picker
// just shows an error toast on the C# side if this module fails.

const IDB_DATABASE_NAME = 'KnockBox.DndMapper';
const IDB_IMAGES_STORE = 'images';

// ── CRC32 ──────────────────────────────────────────────────────────────
// Required for ZIP entry checksums. Lookup table populated once at module
// load. The polynomial 0xEDB88320 is the standard reversed-bits CRC32.
const CRC32_TABLE = (() => {
    const t = new Uint32Array(256);
    for (let i = 0; i < 256; i++) {
        let c = i;
        for (let j = 0; j < 8; j++) c = (c & 1) ? (0xEDB88320 ^ (c >>> 1)) : (c >>> 1);
        t[i] = c >>> 0;
    }
    return t;
})();

function crc32(buf) {
    let c = 0xFFFFFFFF;
    for (let i = 0; i < buf.length; i++) {
        c = (c >>> 8) ^ CRC32_TABLE[(c ^ buf[i]) & 0xFF];
    }
    return (c ^ 0xFFFFFFFF) >>> 0;
}

// ── Deflate via browser-native CompressionStream ───────────────────────
async function deflateRaw(bytes) {
    const cs = new CompressionStream('deflate-raw');
    const writer = cs.writable.getWriter();
    writer.write(bytes);
    writer.close();
    const reader = cs.readable.getReader();
    const chunks = [];
    let total = 0;
    while (true) {
        const { value, done } = await reader.read();
        if (done) break;
        chunks.push(value);
        total += value.length;
    }
    const out = new Uint8Array(total);
    let off = 0;
    for (const c of chunks) { out.set(c, off); off += c.length; }
    return out;
}

// ── ZIP byte structures ────────────────────────────────────────────────

const ZIP_SIG_LOCAL = 0x04034b50;
const ZIP_SIG_CENTRAL = 0x02014b50;
const ZIP_SIG_EOCD = 0x06054b50;
const ZIP_VERSION_NEEDED = 20;
const ZIP_VERSION_MADE_BY = 20;
// MS-DOS date for an arbitrary fixed timestamp (2024-01-01 00:00). VTF
// readers don't check this; using a fixed value keeps output byte-stable
// across runs of the same input.
//
// DOS date layout: bits 9-15 = year-1980, bits 5-8 = month, bits 0-4 = day.
// Month and day must be >=1 or some unzippers reject the entry; a bare
// `year << 9` (which was the previous value) left month=day=0 and pointed
// at year 2013 because the old constant was 0x21 (33), not 44.
const ZIP_FIXED_DOS_DATE = ((2024 - 1980) << 9) | (1 << 5) | 1; // 0x5821
const ZIP_FIXED_DOS_TIME = 0;

function utf8(s) { return new TextEncoder().encode(s); }

function writeUint16(view, off, v) { view.setUint16(off, v & 0xFFFF, true); }
function writeUint32(view, off, v) { view.setUint32(off, v >>> 0, true); }

// Local file header (30 bytes + filename).
function buildLocalHeader(nameBytes, method, crc, compressedSize, uncompressedSize) {
    const buf = new Uint8Array(30 + nameBytes.length);
    const view = new DataView(buf.buffer);
    writeUint32(view, 0, ZIP_SIG_LOCAL);
    writeUint16(view, 4, ZIP_VERSION_NEEDED);
    writeUint16(view, 6, 0); // flags
    writeUint16(view, 8, method); // 0 = stored, 8 = deflate
    writeUint16(view, 10, ZIP_FIXED_DOS_TIME);
    writeUint16(view, 12, ZIP_FIXED_DOS_DATE);
    writeUint32(view, 14, crc);
    writeUint32(view, 18, compressedSize);
    writeUint32(view, 22, uncompressedSize);
    writeUint16(view, 26, nameBytes.length);
    writeUint16(view, 28, 0); // extra length
    buf.set(nameBytes, 30);
    return buf;
}

// Central directory record (46 bytes + filename).
function buildCentralRecord(nameBytes, method, crc, compressedSize, uncompressedSize, localHeaderOffset) {
    const buf = new Uint8Array(46 + nameBytes.length);
    const view = new DataView(buf.buffer);
    writeUint32(view, 0, ZIP_SIG_CENTRAL);
    writeUint16(view, 4, ZIP_VERSION_MADE_BY);
    writeUint16(view, 6, ZIP_VERSION_NEEDED);
    writeUint16(view, 8, 0); // flags
    writeUint16(view, 10, method);
    writeUint16(view, 12, ZIP_FIXED_DOS_TIME);
    writeUint16(view, 14, ZIP_FIXED_DOS_DATE);
    writeUint32(view, 16, crc);
    writeUint32(view, 20, compressedSize);
    writeUint32(view, 24, uncompressedSize);
    writeUint16(view, 28, nameBytes.length);
    writeUint16(view, 30, 0); // extra length
    writeUint16(view, 32, 0); // comment length
    writeUint16(view, 34, 0); // disk number
    writeUint16(view, 36, 0); // internal attrs
    writeUint32(view, 38, 0); // external attrs
    writeUint32(view, 42, localHeaderOffset);
    buf.set(nameBytes, 46);
    return buf;
}

// End-of-central-directory record (22 bytes, no comment).
function buildEocd(entryCount, centralSize, centralOffset) {
    const buf = new Uint8Array(22);
    const view = new DataView(buf.buffer);
    writeUint32(view, 0, ZIP_SIG_EOCD);
    writeUint16(view, 4, 0); // disk
    writeUint16(view, 6, 0); // disk with central
    writeUint16(view, 8, entryCount);
    writeUint16(view, 10, entryCount);
    writeUint32(view, 12, centralSize);
    writeUint32(view, 16, centralOffset);
    writeUint16(view, 20, 0); // comment length
    return buf;
}

// Build the final ZIP from an array of { name, bytes, compress } records.
// `compress` chooses deflate (true) vs. stored (false). Returns a Blob with
// content-type application/zip.
async function buildZip(entries) {
    const localChunks = [];
    const centralChunks = [];
    let offset = 0;
    for (const entry of entries) {
        const nameBytes = utf8(entry.name);
        const raw = entry.bytes;
        const crc = crc32(raw);
        let method = 0;
        let body = raw;
        if (entry.compress) {
            const deflated = await deflateRaw(raw);
            // Don't compress if it didn't help — keeps the archive a touch
            // smaller and matches what a well-behaved server-side packer
            // would do under CompressionLevel.Optimal heuristics.
            if (deflated.length < raw.length) {
                method = 8;
                body = deflated;
            }
        }
        const localHeader = buildLocalHeader(nameBytes, method, crc, body.length, raw.length);
        localChunks.push(localHeader, body);
        centralChunks.push(buildCentralRecord(nameBytes, method, crc, body.length, raw.length, offset));
        offset += localHeader.length + body.length;
    }
    let centralSize = 0;
    for (const c of centralChunks) centralSize += c.length;
    const eocd = buildEocd(entries.length, centralSize, offset);
    const blobParts = [...localChunks, ...centralChunks, eocd];
    return new Blob(blobParts, { type: 'application/zip' });
}

// ── IndexedDB image fetch ──────────────────────────────────────────────
//
// We open the same database the C# layer manages. Read-only transactions
// don't conflict with a concurrent write from the host app, so this is
// safe to call while the user is actively editing.

function openImagesDb() {
    return new Promise((resolve, reject) => {
        const req = indexedDB.open(IDB_DATABASE_NAME);
        req.onsuccess = () => resolve(req.result);
        req.onerror = () => reject(req.error ?? new Error('open failed'));
        req.onblocked = () => reject(new Error('IndexedDB open blocked'));
    });
}

function readBlob(db, key) {
    return new Promise((resolve, reject) => {
        const tx = db.transaction([IDB_IMAGES_STORE], 'readonly');
        const store = tx.objectStore(IDB_IMAGES_STORE);
        const req = store.get(key);
        req.onsuccess = () => resolve(req.result ?? null);
        req.onerror = () => reject(req.error ?? new Error('get failed'));
    });
}

// ── Public API ─────────────────────────────────────────────────────────
//
// Called from C# (DndMapperLibraryService) with the export payload that
// describes every JSON entry (built server-side) and every image ref
// (read client-side from IndexedDB). Returns nothing — the file is
// downloaded directly via the standard <a download> click pattern.
//
// payloadJson schema:
// {
//   "slotName": "...",
//   "fileName": "...",
//   "entries": [ { "path": "manifest.json", "content": "..." }, ... ],
//   "images": [ { "id": "guid-string", "path": "assets/images/{id}.png" } ]
// }
//
// Image bytes never cross the SignalR boundary in this flow — the JSON
// entries are kilobytes, the images stay in IndexedDB until ZIP time.
export async function exportSlot(payloadJson) {
    const payload = typeof payloadJson === 'string' ? JSON.parse(payloadJson) : payloadJson;
    if (!payload) throw new Error('Missing payload');

    const entries = [];

    // JSON shards — compress.
    for (const e of payload.entries ?? []) {
        entries.push({
            name: e.path,
            bytes: utf8(e.content ?? ''),
            compress: true,
        });
    }

    // Image binaries — read from IndexedDB, store uncompressed.
    if ((payload.images?.length ?? 0) > 0) {
        const db = await openImagesDb();
        try {
            for (const img of payload.images) {
                const blob = await readBlob(db, img.id);
                if (!blob) {
                    // Match the server-side fallback: warn-and-skip rather
                    // than abort so a missing image (deleted between snapshot
                    // and export) doesn't lose the whole archive.
                    console.warn(`[VtfPackager] image ${img.id} missing in IndexedDB; skipping.`);
                    continue;
                }
                const bytes = new Uint8Array(await blob.arrayBuffer());
                entries.push({ name: img.path, bytes, compress: false });
            }
        } finally {
            db.close();
        }
    }

    const archive = await buildZip(entries);
    const url = URL.createObjectURL(archive);
    try {
        const a = document.createElement('a');
        a.href = url;
        a.download = payload.fileName || 'slot.vtf';
        a.rel = 'noopener';
        document.body.appendChild(a);
        a.click();
        a.remove();
    } finally {
        // Revoke after a tick so the browser has time to dispatch the
        // download. Synchronous revoke can race with the navigation in
        // some browsers and leave the user with a zero-byte file.
        setTimeout(() => URL.revokeObjectURL(url), 1000);
    }
}
